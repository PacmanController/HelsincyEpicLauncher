// Copyright (c) Helsincy. All rights reserved.

using System.IO.Compression;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Domain.Installations;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Installations;

/// <summary>
/// 修复文件下载器。下载完整资产包到临时目录，仅提取/替换损坏文件。
/// </summary>
public sealed class RepairFileDownloader
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHashingService _hashingService;
    private readonly ILogger _logger = Log.ForContext<RepairFileDownloader>();

    public RepairFileDownloader(IHttpClientFactory httpClientFactory, IHashingService hashingService)
    {
        _httpClientFactory = httpClientFactory;
        _hashingService = hashingService;
    }

    /// <summary>
    /// 下载资产包并修复损坏/缺失文件。
    /// </summary>
    /// <param name="downloadUrl">CDN 下载 URL</param>
    /// <param name="installPath">安装目录</param>
    /// <param name="damagedFiles">需要修复的文件相对路径列表</param>
    /// <param name="manifest">安装清单（用于哈希校验）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>修复结果</returns>
    public async Task<Result<RepairFileResult>> RepairFilesAsync(
        string downloadUrl,
        string installPath,
        IReadOnlyList<string> damagedFiles,
        InstallManifest manifest,
        CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HelsincyRepair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "repair_package");

        try
        {
            // 1. 下载完整资产包
            _logger.Information("开始修复下载 {Url} → {Temp}, 损坏文件数: {Count}",
                downloadUrl, tempFile, damagedFiles.Count);

            var downloadResult = await DownloadFileAsync(downloadUrl, tempFile, ct);
            if (!downloadResult.IsSuccess)
                return Result.Fail<RepairFileResult>(downloadResult.Error!);

            // 2. 提取并替换损坏文件
            var damagedSet = damagedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var manifestLookup = manifest.Files.ToDictionary(
                f => f.RelativePath, f => f, StringComparer.OrdinalIgnoreCase);

            int repairedCount = 0;
            var failedFiles = new List<string>();

            if (IsZipFile(tempFile))
            {
                repairedCount = await RepairFromZipAsync(
                    tempFile, installPath, damagedSet, manifestLookup,
                    failedFiles, ct);
            }
            else
            {
                RepairSingleFile(
                    tempFile, installPath, manifest, damagedSet,
                    ref repairedCount, failedFiles);
            }

            _logger.Information("修复下载完成: 修复 {Repaired} 个, 失败 {Failed} 个",
                repairedCount, failedFiles.Count);

            return Result.Ok(new RepairFileResult
            {
                RepairedCount = repairedCount,
                FailedFiles = failedFiles,
            });
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("修复下载被取消");
            return Result.Fail<RepairFileResult>(new Error
            {
                Code = "REPAIR_CANCELLED",
                UserMessage = "修复已取消",
                TechnicalMessage = "OperationCanceledException during repair download",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private async Task<int> RepairFromZipAsync(
        string zipPath,
        string installPath,
        HashSet<string> damagedSet,
        Dictionary<string, ManifestFileEntry> manifestLookup,
        List<string> failedFiles,
        CancellationToken ct)
    {
        int repairedCount = 0;
        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            ct.ThrowIfCancellationRequested();

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (!damagedSet.Contains(relativePath))
                continue;

            var destPath = Path.Combine(installPath, relativePath);
            var tmpDest = destPath + ".repair_tmp";

            try
            {
                // Zip Slip 防护
                var fullDest = Path.GetFullPath(destPath);
                if (!fullDest.StartsWith(Path.GetFullPath(installPath), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning("Zip Slip 检测到恶意路径 {Entry}, 跳过", entry.FullName);
                    failedFiles.Add(relativePath);
                    continue;
                }

                // 确保目标目录存在
                var destDir = Path.GetDirectoryName(destPath)!;
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                // 解压到临时文件
                entry.ExtractToFile(tmpDest, overwrite: true);

                // 校验哈希
                if (manifestLookup.TryGetValue(relativePath, out var expected))
                {
                    var hashResult = await _hashingService.ComputeHashAsync(tmpDest, ct);
                    if (!hashResult.IsSuccess ||
                        !string.Equals(hashResult.Value, expected.Hash, StringComparison.OrdinalIgnoreCase))
                    {
                        failedFiles.Add(relativePath);
                        _logger.Warning("修复文件哈希不匹配 {File}, 期望={Expected}, 实际={Actual}",
                            relativePath, expected.Hash, hashResult.Value ?? "N/A");
                        SafeDelete(tmpDest);
                        continue;
                    }
                }

                // 原子替换：删除旧文件 → 移动新文件
                if (File.Exists(destPath))
                    File.Delete(destPath);
                File.Move(tmpDest, destPath);
                repairedCount++;
                _logger.Debug("已修复文件 {File}", relativePath);
            }
            catch (Exception ex)
            {
                failedFiles.Add(relativePath);
                _logger.Warning(ex, "修复文件失败 {File}", relativePath);
                SafeDelete(tmpDest);
            }
        }

        return repairedCount;
    }

    private static void RepairSingleFile(
        string tempFile,
        string installPath,
        InstallManifest manifest,
        HashSet<string> damagedSet,
        ref int repairedCount,
        List<string> failedFiles)
    {
        // 单文件资产：整个文件就是资产本身
        var fileEntry = manifest.Files.FirstOrDefault(f => damagedSet.Contains(f.RelativePath));
        if (fileEntry is null)
            return;

        var destPath = Path.Combine(installPath, fileEntry.RelativePath);
        try
        {
            var destDir = Path.GetDirectoryName(destPath)!;
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(tempFile, destPath, overwrite: true);
            repairedCount++;
        }
        catch (Exception)
        {
            failedFiles.Add(fileEntry.RelativePath);
        }
    }

    private async Task<Result> DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("ChunkDownload");
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await contentStream.CopyToAsync(fileStream, ct);

            _logger.Debug("修复包下载完成 {Url} → {Path}", url, destPath);
            return Result.Ok();
        }
        catch (HttpRequestException ex)
        {
            _logger.Error(ex, "修复包下载 HTTP 错误 {Url}", url);
            return Result.Fail(new Error
            {
                Code = "REPAIR_DOWNLOAD_FAILED",
                UserMessage = "修复包下载失败，请检查网络",
                TechnicalMessage = $"HTTP error downloading {url}: {ex.Message}",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
        catch (IOException ex)
        {
            _logger.Error(ex, "修复包写入磁盘失败 {Path}", destPath);
            return Result.Fail(new Error
            {
                Code = "REPAIR_IO_FAILED",
                UserMessage = "修复包写入磁盘失败",
                TechnicalMessage = $"IO error writing {destPath}: {ex.Message}",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    private static bool IsZipFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[4];
            if (stream.Read(header, 0, 4) < 4) return false;
            // ZIP magic number: PK\x03\x04
            return header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* 忽略清理错误 */ }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* 忽略清理错误 */ }
    }
}

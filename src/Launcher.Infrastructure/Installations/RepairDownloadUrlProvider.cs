// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Installations;

/// <summary>
/// 修复下载 URL 提供者实现。通过 IFabDownloadInfoProvider 获取最新 CDN 链接。
/// 通过依赖倒置：接口在 Installations.Contracts，实现在 Infrastructure 层。
/// </summary>
public sealed class RepairDownloadUrlProvider : IRepairDownloadUrlProvider
{
    private readonly IFabDownloadInfoProvider _downloadInfoProvider;
    private readonly ILogger _logger = Log.ForContext<RepairDownloadUrlProvider>();

    public RepairDownloadUrlProvider(IFabDownloadInfoProvider downloadInfoProvider)
    {
        _downloadInfoProvider = downloadInfoProvider;
    }

    public async Task<Result<RepairDownloadInfo>> GetDownloadInfoAsync(string assetId, CancellationToken ct)
    {
        _logger.Information("获取修复下载链接 {AssetId}", assetId);

        var result = await _downloadInfoProvider.GetDownloadInfoAsync(assetId, ct);
        if (!result.IsSuccess)
        {
            _logger.Warning("获取修复下载链接失败 {AssetId}: {Error}", assetId, result.Error?.TechnicalMessage);
            return Result.Fail<RepairDownloadInfo>(new Error
            {
                Code = "REPAIR_URL_FAILED",
                UserMessage = "无法获取修复所需的下载链接，请检查网络并重试",
                TechnicalMessage = result.Error?.TechnicalMessage ?? "FabApiClient.GetDownloadInfoAsync failed",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }

        var info = result.Value!;
        _logger.Debug("获取修复下载链接成功 {AssetId}: {Url}", assetId, info.DownloadUrl);

        return Result.Ok(new RepairDownloadInfo
        {
            DownloadUrl = info.DownloadUrl,
            FileName = info.FileName,
            FileSize = info.FileSize,
        });
    }
}

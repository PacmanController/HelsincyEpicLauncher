// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Domain.Installations;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Installations;

/// <summary>
/// 安装命令服务实现。编排安装/卸载/修复流程。
/// </summary>
public sealed class InstallCommandService : IInstallCommandService
{
    private readonly IInstallationRepository _repository;
    private readonly InstallWorker _worker;
    private readonly IIntegrityVerifier _verifier;
    private readonly IRepairDownloadUrlProvider _urlProvider;
    private readonly RepairFileDownloader _repairDownloader;
    private readonly ILogger _logger = Log.ForContext<InstallCommandService>();

    /// <summary>安装完成事件</summary>
    public event Action<InstallationCompletedEvent>? InstallationCompleted;

    /// <summary>安装失败事件</summary>
    public event Action<InstallationFailedEvent>? InstallationFailed;

    /// <summary>卸载完成事件</summary>
    public event Action<UninstallCompletedEvent>? UninstallCompleted;

    /// <summary>修复完成事件</summary>
    public event Action<RepairCompletedEvent>? RepairCompleted;

    public InstallCommandService(
        IInstallationRepository repository,
        InstallWorker worker,
        IIntegrityVerifier verifier,
        IRepairDownloadUrlProvider urlProvider,
        RepairFileDownloader repairDownloader)
    {
        _repository = repository;
        _worker = worker;
        _verifier = verifier;
        _urlProvider = urlProvider;
        _repairDownloader = repairDownloader;
    }

    public async Task<Result> InstallAsync(InstallRequest request, CancellationToken ct)
    {
        _logger.Information("开始安装流程 {AssetId} → {InstallPath}", request.AssetId, request.InstallPath);

        // 检查是否已安装
        var existing = await _repository.GetByAssetIdAsync(request.AssetId, ct);
        if (existing is not null && existing.State == InstallState.Installed)
        {
            return Result.Fail(new Error
            {
                Code = "INSTALL_ALREADY_EXISTS",
                UserMessage = "该资产已安装",
                TechnicalMessage = $"资产 {request.AssetId} 已存在安装记录",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        // 创建安装记录
        var installation = existing ?? new Installation(
            request.AssetId,
            request.AssetName,
            request.Version,
            request.InstallPath,
            request.AssetType);

        var transitionResult = installation.TransitionTo(InstallState.Installing);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        if (existing is null)
            await _repository.InsertAsync(installation, ct);
        else
            await _repository.UpdateAsync(installation, ct);

        // 执行安装
        var installResult = await _worker.ExecuteAsync(installation, request.SourcePath, null, ct);

        if (installResult.IsSuccess)
        {
            installation.TransitionTo(InstallState.Installed);
            await _repository.UpdateAsync(installation, ct);
            InstallationCompleted?.Invoke(new InstallationCompletedEvent(request.AssetId, request.InstallPath));
            _logger.Information("安装成功 {AssetId}", request.AssetId);
        }
        else
        {
            installation.TransitionTo(InstallState.Failed);
            installation.SetError(installResult.Error?.TechnicalMessage ?? "Unknown error");
            await _repository.UpdateAsync(installation, ct);
            InstallationFailed?.Invoke(new InstallationFailedEvent(request.AssetId, installResult.Error?.UserMessage ?? "安装失败"));
            _logger.Warning("安装失败 {AssetId}: {Error}", request.AssetId, installResult.Error?.TechnicalMessage);
        }

        return installResult;
    }

    public async Task<Result> UninstallAsync(string assetId, CancellationToken ct)
    {
        _logger.Information("开始卸载 {AssetId}", assetId);

        var installation = await _repository.GetByAssetIdAsync(assetId, ct);
        if (installation is null)
        {
            return Result.Fail(new Error
            {
                Code = "INSTALL_NOT_FOUND",
                UserMessage = "未找到该资产的安装记录",
                TechnicalMessage = $"AssetId {assetId} 不存在",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        var transitionResult = installation.TransitionTo(InstallState.Uninstalling);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        await _repository.UpdateAsync(installation, ct);

        try
        {
            // 删除安装目录
            if (Directory.Exists(installation.InstallPath))
            {
                Directory.Delete(installation.InstallPath, recursive: true);
                _logger.Debug("已删除安装目录 {Path}", installation.InstallPath);
            }

            // 删除 Manifest
            await _repository.DeleteManifestAsync(assetId, ct);

            // 删除数据库记录
            await _repository.DeleteAsync(installation.Id, ct);

            UninstallCompleted?.Invoke(new UninstallCompletedEvent(assetId));
            _logger.Information("卸载完成 {AssetId}", assetId);
            return Result.Ok();
        }
        catch (IOException ex)
        {
            installation.TransitionTo(InstallState.Failed);
            installation.SetError(ex.Message);
            await _repository.UpdateAsync(installation, ct);

            _logger.Error(ex, "卸载失败 {AssetId}", assetId);
            return Result.Fail(new Error
            {
                Code = "UNINSTALL_IO_ERROR",
                UserMessage = "卸载时发生错误，请关闭正在使用该文件夹的程序后重试",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    public async Task<Result> RepairAsync(string assetId, CancellationToken ct)
    {
        _logger.Information("开始修复 {AssetId}", assetId);

        // 1. 查找安装记录
        var installation = await _repository.GetByAssetIdAsync(assetId, ct);
        if (installation is null)
        {
            return Result.Fail(new Error
            {
                Code = "INSTALL_NOT_FOUND",
                UserMessage = "未找到该资产的安装记录",
                TechnicalMessage = $"AssetId {assetId} 不存在",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        var transitionResult = installation.TransitionTo(InstallState.Repairing);
        if (!transitionResult.IsSuccess)
            return transitionResult;

        await _repository.UpdateAsync(installation, ct);

        // 2. 读取 Manifest
        var manifest = await _repository.GetManifestAsync(assetId, ct);
        if (manifest is null)
        {
            installation.TransitionTo(InstallState.Failed);
            installation.SetError("Manifest 不存在，无法修复");
            await _repository.UpdateAsync(installation, ct);
            return Result.Fail(new Error
            {
                Code = "REPAIR_NO_MANIFEST",
                UserMessage = "安装清单不存在，请尝试重新安装",
                TechnicalMessage = $"Manifest not found for {assetId}",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        // 3. 执行完整性校验
        var verifyResult = await _verifier.VerifyInstallationAsync(installation.InstallPath, manifest, null, ct);
        if (!verifyResult.IsSuccess)
        {
            installation.TransitionTo(InstallState.Failed);
            installation.SetError(verifyResult.Error?.TechnicalMessage ?? "校验失败");
            await _repository.UpdateAsync(installation, ct);
            return Result.Fail(verifyResult.Error!);
        }

        var report = verifyResult.Value!;
        if (report.IsValid)
        {
            // 无损坏文件
            installation.TransitionTo(InstallState.Installed);
            installation.ClearError();
            await _repository.UpdateAsync(installation, ct);
            RepairCompleted?.Invoke(new RepairCompletedEvent(assetId, 0));
            _logger.Information("校验通过，无需修复 {AssetId}", assetId);
            return Result.Ok();
        }

        // 4. 获取新鲜下载 URL
        var damagedFiles = report.MissingFiles.Concat(report.CorruptedFiles).ToList();
        _logger.Warning("发现 {Count} 个损坏/缺失文件 {AssetId}: Missing={Missing}, Corrupted={Corrupted}",
            damagedFiles.Count, assetId, report.MissingFiles.Count, report.CorruptedFiles.Count);

        var urlResult = await _urlProvider.GetDownloadInfoAsync(assetId, ct);
        if (!urlResult.IsSuccess)
        {
            installation.TransitionTo(InstallState.NeedsRepair);
            installation.SetError("无法获取下载链接");
            await _repository.UpdateAsync(installation, ct);
            return Result.Fail(urlResult.Error!);
        }

        // 5. 下载并替换损坏文件
        var repairResult = await _repairDownloader.RepairFilesAsync(
            urlResult.Value!.DownloadUrl, installation.InstallPath, damagedFiles, manifest, ct);

        if (!repairResult.IsSuccess)
        {
            installation.TransitionTo(InstallState.NeedsRepair);
            installation.SetError(repairResult.Error?.TechnicalMessage ?? "修复下载失败");
            await _repository.UpdateAsync(installation, ct);
            return Result.Fail(repairResult.Error!);
        }

        // 6. 二次校验
        var reVerifyResult = await _verifier.VerifyInstallationAsync(installation.InstallPath, manifest, null, ct);
        if (!reVerifyResult.IsSuccess || !reVerifyResult.Value!.IsValid)
        {
            var stillDamaged = (reVerifyResult.Value?.MissingFiles.Count ?? 0)
                             + (reVerifyResult.Value?.CorruptedFiles.Count ?? 0);
            installation.TransitionTo(InstallState.NeedsRepair);
            installation.SetError($"二次校验仍有 {stillDamaged} 个文件异常");
            await _repository.UpdateAsync(installation, ct);
            return Result.Fail(new Error
            {
                Code = "REPAIR_INCOMPLETE",
                UserMessage = $"修复未完全成功，仍有 {stillDamaged} 个文件异常",
                TechnicalMessage = $"Re-verification failed: {stillDamaged} files still damaged for {assetId}",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }

        // 7. 修复成功
        installation.TransitionTo(InstallState.Installed);
        installation.ClearError();
        await _repository.UpdateAsync(installation, ct);

        var repairedCount = repairResult.Value!.RepairedCount;
        RepairCompleted?.Invoke(new RepairCompletedEvent(assetId, repairedCount));
        _logger.Information("修复完成 {AssetId}, 修复 {Count} 个文件", assetId, repairedCount);
        return Result.Ok();
    }
}

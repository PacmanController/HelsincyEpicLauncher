// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Downloads.Contracts;
using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.FabLibrary;

/// <summary>
/// Fab 资产操作服务实现。发起下载、刷新缓存。
/// </summary>
public sealed class FabAssetCommandService : IFabAssetCommandService
{
    private readonly FabApiClient _apiClient;
    private readonly IDownloadCommandService _downloadCommandService;
    private readonly IFabCatalogReadService _catalogReadService;
    private readonly ILogger _logger = Log.ForContext<FabAssetCommandService>();

    public FabAssetCommandService(
        FabApiClient apiClient,
        IDownloadCommandService downloadCommandService,
        IFabCatalogReadService catalogReadService)
    {
        _apiClient = apiClient;
        _downloadCommandService = downloadCommandService;
        _catalogReadService = catalogReadService;
    }

    public async Task<Result<Guid>> DownloadAssetAsync(string assetId, string installPath, CancellationToken ct)
    {
        _logger.Information("发起 Fab 资产下载 {AssetId} → {Path}", assetId, installPath);

        // 获取下载信息
        var downloadInfoResult = await _apiClient.GetDownloadInfoAsync(assetId, ct);
        if (!downloadInfoResult.IsSuccess)
            return Result.Fail<Guid>(downloadInfoResult.Error!);

        var info = downloadInfoResult.Value!;

        // 获取资产标题
        var detailResult = await _catalogReadService.GetDetailAsync(assetId, ct);
        var assetName = detailResult.IsSuccess ? detailResult.Value!.Title : assetId;

        // 创建下载任务
        var request = new StartDownloadRequest
        {
            AssetId = assetId,
            AssetName = assetName,
            DownloadUrl = info.DownloadUrl,
            DestinationPath = installPath,
            TotalBytes = info.FileSize,
            Priority = 0,
        };

        var startResult = await _downloadCommandService.StartAsync(request, ct);
        if (startResult.IsSuccess)
        {
            _logger.Information("Fab 资产下载已创建 {AssetId}, TaskId={TaskId}", assetId, startResult.Value);
            return Result.Ok(startResult.Value.Value);
        }
        else
        {
            _logger.Error("Fab 资产下载创建失败 {AssetId}: {Error}", assetId, startResult.Error?.TechnicalMessage);
            return Result.Fail<Guid>(startResult.Error!);
        }
    }

    public async Task<Result> RefreshCacheAsync(CancellationToken ct)
    {
        _logger.Information("刷新 Fab 已拥有资产缓存");

        var result = await _catalogReadService.GetOwnedAssetsAsync(ct);
        if (!result.IsSuccess)
        {
            _logger.Warning("刷新缓存失败: {Error}", result.Error?.TechnicalMessage);
            return Result.Fail(result.Error!);
        }

        _logger.Information("缓存刷新完成，{Count} 个已拥有资产", result.Value!.Count);
        return Result.Ok();
    }
}

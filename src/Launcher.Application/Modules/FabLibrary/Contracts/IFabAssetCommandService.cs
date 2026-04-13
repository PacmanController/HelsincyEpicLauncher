// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Downloads;
using Launcher.Shared;

namespace Launcher.Application.Modules.FabLibrary.Contracts;

/// <summary>
/// Fab 资产操作服务。
/// </summary>
public interface IFabAssetCommandService
{
    /// <summary>发起资产下载（会调用 Downloads 模块）</summary>
    Task<Result<DownloadTaskId>> DownloadAssetAsync(string assetId, string installPath, CancellationToken ct);

    /// <summary>刷新本地已拥有资产缓存</summary>
    Task<Result> RefreshCacheAsync(CancellationToken ct);
}

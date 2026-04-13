// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.FabLibrary.Contracts;

/// <summary>
/// Fab 资产目录查询服务。
/// </summary>
public interface IFabCatalogReadService
{
    /// <summary>搜索 Fab 资产</summary>
    Task<Result<PagedResult<FabAssetSummary>>> SearchAsync(FabSearchQuery query, CancellationToken ct);

    /// <summary>获取资产详情</summary>
    Task<Result<FabAssetDetail>> GetDetailAsync(string assetId, CancellationToken ct);

    /// <summary>获取已拥有的资产列表</summary>
    Task<Result<IReadOnlyList<FabAssetSummary>>> GetOwnedAssetsAsync(CancellationToken ct);

    /// <summary>获取资产分类列表</summary>
    Task<Result<IReadOnlyList<AssetCategoryInfo>>> GetCategoriesAsync(CancellationToken ct);
}

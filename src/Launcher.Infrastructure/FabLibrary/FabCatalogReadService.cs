// Copyright (c) Helsincy. All rights reserved.

using System.Collections.Concurrent;
using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.FabLibrary;

/// <summary>
/// Fab 目录查询服务实现。包含 5 分钟内存缓存。
/// </summary>
internal sealed class FabCatalogReadService : IFabCatalogReadService
{
    private readonly FabApiClient _apiClient;
    private readonly EpicOwnedFabCatalogClient _ownedFallbackClient;
    private readonly IInstallReadService _installReadService;
    private readonly ILogger _logger = Log.ForContext<FabCatalogReadService>();

    /// <summary>搜索结果缓存（key = 查询 hash, value = (缓存时间, 结果)）</summary>
    private readonly ConcurrentDictionary<string, (DateTime CachedAt, object Result)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public FabCatalogReadService(FabApiClient apiClient, EpicOwnedFabCatalogClient ownedFallbackClient, IInstallReadService installReadService)
    {
        _apiClient = apiClient;
        _ownedFallbackClient = ownedFallbackClient;
        _installReadService = installReadService;
    }

    public async Task<Result<PagedResult<FabAssetSummary>>> SearchAsync(FabSearchQuery query, CancellationToken ct)
    {
        var cacheKey = $"search:{query.Keyword}:{query.Category}:{query.EngineVersion}:{query.SortOrder}:{query.Page}:{query.PageSize}";

        if (TryGetCached<PagedResult<FabAssetSummary>>(cacheKey, out var cached))
        {
            _logger.Debug("搜索命中缓存 {Key}", cacheKey);
            return Result.Ok(cached!);
        }

        var apiResult = await _apiClient.SearchAsync(query, ct);
        if (!apiResult.IsSuccess)
        {
            if (string.Equals(apiResult.Error?.Code, "FAB_BROWSER_CHALLENGE_BLOCKED", StringComparison.Ordinal))
            {
                _logger.Information("Fab 搜索切换到 Epic 后端已拥有资产回退 | Keyword={Keyword}", query.Keyword);
                var fallbackResult = await _ownedFallbackClient.SearchOwnedAsync(query, ct);
                if (fallbackResult.IsSuccess)
                {
                    _logger.Information("Fab Epic 回退搜索完成 | Returned={Count} | Total={Total}",
                        fallbackResult.Value!.Items.Count,
                        fallbackResult.Value.TotalCount);
                }

                return fallbackResult;
            }

            return Result.Fail<PagedResult<FabAssetSummary>>(apiResult.Error!);
        }

        var response = apiResult.Value!;
        var installedAssets = await GetInstalledAssetIds(ct);

        var items = response.Items.Select(dto => MapToSummary(dto, installedAssets)).ToList();
        var pagedResult = new PagedResult<FabAssetSummary>
        {
            Items = items,
            TotalCount = response.TotalCount,
            Page = response.Page,
            PageSize = response.PageSize,
        };

        SetCache(cacheKey, pagedResult);
        _logger.Information("搜索完成 keyword={Keyword}, 返回 {Count}/{Total}", query.Keyword, items.Count, response.TotalCount);

        return Result.Ok(pagedResult);
    }

    public async Task<Result<FabAssetDetail>> GetDetailAsync(string assetId, CancellationToken ct)
    {
        var cacheKey = $"detail:{assetId}";

        if (TryGetCached<FabAssetDetail>(cacheKey, out var cached))
            return Result.Ok(cached!);

        var apiResult = await _apiClient.GetDetailAsync(assetId, ct);
        if (!apiResult.IsSuccess)
        {
            if (string.Equals(apiResult.Error?.Code, "FAB_BROWSER_CHALLENGE_BLOCKED", StringComparison.Ordinal))
            {
                _logger.Information("Fab 详情切换到 Epic 后端已拥有资产回退 | AssetId={AssetId}", assetId);
                return await _ownedFallbackClient.GetDetailAsync(assetId, ct);
            }

            return Result.Fail<FabAssetDetail>(apiResult.Error!);
        }

        var dto = apiResult.Value!;
        var installedAssets = await GetInstalledAssetIds(ct);

        var detail = new FabAssetDetail
        {
            AssetId = dto.AssetId,
            Title = dto.Title,
            Description = dto.Description,
            Author = dto.Author,
            Price = dto.Price,
            Rating = dto.Rating,
            RatingCount = dto.RatingCount,
            DownloadSize = dto.DownloadSize,
            LatestVersion = dto.LatestVersion,
            UpdatedAt = dto.UpdatedAt,
            Screenshots = dto.Screenshots,
            SupportedEngineVersions = dto.SupportedEngineVersions,
            Tags = dto.Tags,
            TechnicalDetails = dto.TechnicalDetails,
            IsOwned = dto.IsOwned,
            IsInstalled = installedAssets.Contains(dto.AssetId),
        };

        SetCache(cacheKey, detail);
        return Result.Ok(detail);
    }

    public async Task<Result<IReadOnlyList<FabAssetSummary>>> GetOwnedAssetsAsync(CancellationToken ct)
    {
        const string cacheKey = "owned";

        if (TryGetCached<IReadOnlyList<FabAssetSummary>>(cacheKey, out var cached))
            return Result.Ok(cached!);

        var apiResult = await _apiClient.GetOwnedAssetsAsync(ct);
        if (!apiResult.IsSuccess)
        {
            if (string.Equals(apiResult.Error?.Code, "FAB_BROWSER_CHALLENGE_BLOCKED", StringComparison.Ordinal))
            {
                _logger.Information("Fab 已拥有资产切换到 Epic 后端回退");
                return await _ownedFallbackClient.GetOwnedAssetsAsync(ct);
            }

            return Result.Fail<IReadOnlyList<FabAssetSummary>>(apiResult.Error!);
        }

        var installedAssets = await GetInstalledAssetIds(ct);
        var items = apiResult.Value!.Items
            .Select(dto => MapToSummary(dto, installedAssets))
            .ToList();

        SetCache(cacheKey, items);
        _logger.Information("已拥有资产加载完成：{Count} 个", items.Count);

        return Result.Ok<IReadOnlyList<FabAssetSummary>>(items);
    }

    public async Task<Result<IReadOnlyList<AssetCategoryInfo>>> GetCategoriesAsync(CancellationToken ct)
    {
        const string cacheKey = "categories";

        if (TryGetCached<IReadOnlyList<AssetCategoryInfo>>(cacheKey, out var cached))
            return Result.Ok(cached!);

        var apiResult = await _apiClient.GetCategoriesAsync(ct);
        if (!apiResult.IsSuccess)
        {
            if (string.Equals(apiResult.Error?.Code, "FAB_BROWSER_CHALLENGE_BLOCKED", StringComparison.Ordinal))
            {
                _logger.Information("Fab 分类切换到 Epic 后端空分类回退");
                return await _ownedFallbackClient.GetCategoriesAsync(ct);
            }

            return Result.Fail<IReadOnlyList<AssetCategoryInfo>>(apiResult.Error!);
        }

        var items = apiResult.Value!.Items
            .Select(dto => new AssetCategoryInfo
            {
                Id = dto.Id,
                Name = dto.Name,
                AssetCount = dto.AssetCount,
            })
            .ToList();

        SetCache(cacheKey, items);
        return Result.Ok<IReadOnlyList<AssetCategoryInfo>>(items);
    }

    private static FabAssetSummary MapToSummary(FabAssetDto dto, HashSet<string> installedAssets)
        => new()
        {
            AssetId = dto.AssetId,
            Title = dto.Title,
            ThumbnailUrl = dto.ThumbnailUrl,
            Category = dto.Category,
            Author = dto.Author,
            Price = dto.Price,
            Rating = dto.Rating,
            IsOwned = dto.IsOwned,
            IsInstalled = installedAssets.Contains(dto.AssetId),
            SupportedEngineVersions = dto.SupportedEngineVersions,
        };

    private async Task<HashSet<string>> GetInstalledAssetIds(CancellationToken ct)
    {
        var installed = await _installReadService.GetInstalledAsync(ct);
        return installed.Select(i => i.AssetId).ToHashSet();
    }

    private bool TryGetCached<T>(string key, out T? value) where T : class
    {
        if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.CachedAt < CacheDuration)
        {
            value = entry.Result as T;
            return value is not null;
        }

        value = null;
        return false;
    }

    private void SetCache(string key, object value)
    {
        _cache[key] = (DateTime.UtcNow, value);

        // 清理过期缓存
        var expired = _cache.Where(kv => DateTime.UtcNow - kv.Value.CachedAt >= CacheDuration).Select(kv => kv.Key).ToList();
        foreach (var k in expired)
            _cache.TryRemove(k, out _);
    }
}

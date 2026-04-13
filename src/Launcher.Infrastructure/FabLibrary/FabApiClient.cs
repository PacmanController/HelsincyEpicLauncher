// Copyright (c) Helsincy. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Shared;
using Polly;
using Polly.Retry;
using Serilog;

namespace Launcher.Infrastructure.FabLibrary;

/// <summary>
/// Fab Marketplace HTTP API 客户端。封装 API 调用、认证头注入、Polly 韧性策略。
/// </summary>
public sealed class FabApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly ILogger _logger = Log.ForContext<FabApiClient>();
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public FabApiClient(IHttpClientFactory httpClientFactory, IAuthService authService)
    {
        _httpClient = httpClientFactory.CreateClient("FabApi");
        _authService = authService;

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnRetry = args =>
                {
                    _logger.Warning("Fab API 重试 #{Attempt}, 延迟 {Delay}ms",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }

    /// <summary>搜索 Fab 资产</summary>
    internal async Task<Result<FabSearchResponse>> SearchAsync(FabSearchQuery query, CancellationToken ct)
    {
        var url = BuildSearchUrl(query);
        return await GetAsync<FabSearchResponse>(url, ct);
    }

    /// <summary>获取资产详情</summary>
    internal async Task<Result<FabAssetDetailDto>> GetDetailAsync(string assetId, CancellationToken ct)
    {
        return await GetAsync<FabAssetDetailDto>($"/v1/assets/{Uri.EscapeDataString(assetId)}", ct);
    }

    /// <summary>获取已拥有资产列表</summary>
    internal async Task<Result<FabOwnedAssetsResponse>> GetOwnedAssetsAsync(CancellationToken ct)
    {
        return await GetAsync<FabOwnedAssetsResponse>("/v1/assets/owned", ct);
    }

    /// <summary>获取分类列表</summary>
    internal async Task<Result<FabCategoriesResponse>> GetCategoriesAsync(CancellationToken ct)
    {
        return await GetAsync<FabCategoriesResponse>("/v1/categories", ct);
    }

    /// <summary>获取资产下载链接</summary>
    internal async Task<Result<FabDownloadInfoDto>> GetDownloadInfoAsync(string assetId, CancellationToken ct)
    {
        return await GetAsync<FabDownloadInfoDto>($"/v1/assets/{Uri.EscapeDataString(assetId)}/download", ct);
    }

    private async Task<Result<T>> GetAsync<T>(string url, CancellationToken ct) where T : class
    {
        try
        {
            var tokenResult = await _authService.GetAccessTokenAsync(ct);
            if (!tokenResult.IsSuccess)
            {
                return Result.Fail<T>(new Error
                {
                    Code = "FAB_AUTH_FAILED",
                    UserMessage = "认证失败，请重新登录",
                    TechnicalMessage = tokenResult.Error?.TechnicalMessage ?? "Token unavailable",
                    CanRetry = true,
                    Severity = ErrorSeverity.Error,
                });
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);

            var response = await _pipeline.ExecuteAsync(
                async token =>
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.Value);
                    return await _httpClient.SendAsync(req, token);
                },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.Warning("Fab API 错误 {StatusCode}: {Url} → {Body}", (int)response.StatusCode, url, body);
                return Result.Fail<T>(new Error
                {
                    Code = $"FAB_HTTP_{(int)response.StatusCode}",
                    UserMessage = $"Fab 服务请求失败 ({(int)response.StatusCode})",
                    TechnicalMessage = $"HTTP {(int)response.StatusCode}: {body}",
                    CanRetry = (int)response.StatusCode >= 500,
                    Severity = ErrorSeverity.Error,
                });
            }

            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            if (result is null)
            {
                return Result.Fail<T>(new Error
                {
                    Code = "FAB_DESERIALIZE_FAILED",
                    UserMessage = "服务响应解析失败",
                    TechnicalMessage = $"Deserialization returned null for {url}",
                    CanRetry = true,
                    Severity = ErrorSeverity.Error,
                });
            }

            return Result.Ok(result);
        }
        catch (OperationCanceledException)
        {
            return Result.Fail<T>(new Error
            {
                Code = "FAB_CANCELLED",
                UserMessage = "请求已取消",
                TechnicalMessage = "OperationCanceledException",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Fab API 异常 {Url}", url);
            return Result.Fail<T>(new Error
            {
                Code = "FAB_REQUEST_ERROR",
                UserMessage = "Fab 服务请求异常",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    private static string BuildSearchUrl(FabSearchQuery query)
    {
        var parts = new List<string> { $"page={query.Page}", $"pageSize={query.PageSize}" };
        if (!string.IsNullOrWhiteSpace(query.Keyword))
            parts.Add($"q={Uri.EscapeDataString(query.Keyword)}");
        if (!string.IsNullOrWhiteSpace(query.Category))
            parts.Add($"category={Uri.EscapeDataString(query.Category)}");
        if (!string.IsNullOrWhiteSpace(query.EngineVersion))
            parts.Add($"engineVersion={Uri.EscapeDataString(query.EngineVersion)}");
        parts.Add($"sort={query.SortOrder.ToString().ToLowerInvariant()}");
        return $"/v1/assets/search?{string.Join("&", parts)}";
    }
}

// ===== API 响应 DTO =====

internal sealed class FabSearchResponse
{
    public List<FabAssetDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

internal sealed class FabOwnedAssetsResponse
{
    public List<FabAssetDto> Items { get; set; } = [];
}

internal sealed class FabCategoriesResponse
{
    public List<FabCategoryDto> Items { get; set; } = [];
}

internal sealed class FabAssetDto
{
    public string AssetId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public bool IsOwned { get; set; }
    public List<string> SupportedEngineVersions { get; set; } = [];
}

internal sealed class FabAssetDetailDto
{
    public string AssetId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public long DownloadSize { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public List<string> Screenshots { get; set; } = [];
    public List<string> SupportedEngineVersions { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? TechnicalDetails { get; set; }
    public bool IsOwned { get; set; }
}

internal sealed class FabDownloadInfoDto
{
    public string AssetId { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Version { get; set; } = string.Empty;
}

internal sealed class FabCategoryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AssetCount { get; set; }
}

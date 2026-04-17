// Copyright (c) Helsincy. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Launcher.Shared.Logging;
using Launcher.Infrastructure.Network;
using Polly;
using Serilog;

namespace Launcher.Infrastructure.EngineVersions;

/// <summary>
/// Epic Games 引擎版本 API 客户端。
/// </summary>
public sealed class EngineVersionApiClient
{
    private static readonly ILogger Logger = Log.ForContext<EngineVersionApiClient>();

    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    private static readonly JsonSerializerOptions JsonOptions = Launcher.Shared.JsonDefaults.SnakeCaseLower;

    public EngineVersionApiClient(IHttpClientFactory httpClientFactory, IAuthService authService)
    {
        _httpClient = httpClientFactory.CreateClient("EngineVersionApi");
        _authService = authService;

        _pipeline = HttpResiliencePipelineFactory.CreateDefault(Logger);
    }

    /// <summary>获取可用引擎版本列表</summary>
    internal async Task<Result<EngineVersionsResponse>> GetAvailableVersionsAsync(CancellationToken ct)
    {
        try
        {
            await InjectAuthHeaderAsync(ct);

            var response = await _pipeline.ExecuteAsync(
                async token => await _httpClient.GetAsync("engine/versions", token),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);

                if (WebsiteChallengeDetector.IsBlocked(response, body))
                {
                    Logger.Warning("引擎版本列表被网站挑战拦截");
                    return Result.Fail<EngineVersionsResponse>(new Error
                    {
                        Code = "ENGINE_BROWSER_CHALLENGE_BLOCKED",
                        UserMessage = "引擎版本在线列表当前被网站浏览器验证拦截，当前版本暂时无法直接加载。",
                        TechnicalMessage = $"HTTP {(int)response.StatusCode}: browser challenge blocked engine version endpoint. {LogSanitizer.SanitizeHttpBody(body, 400)}",
                        CanRetry = false,
                        Severity = ErrorSeverity.Warning,
                    });
                }

                Logger.Warning("引擎版本列表请求失败: {Status} | Body={Body}", response.StatusCode, LogSanitizer.SanitizeHttpBody(body, 400));
                return Result.Fail<EngineVersionsResponse>(new Error
                {
                    Code = "ENGINE_API_ERROR",
                    UserMessage = "获取引擎版本列表失败",
                    TechnicalMessage = $"HTTP {(int)response.StatusCode}: {LogSanitizer.SanitizeHttpBody(body, 400)}",
                    CanRetry = true,
                    Severity = ErrorSeverity.Warning,
                });
            }

            var result = await response.Content.ReadFromJsonAsync<EngineVersionsResponse>(JsonOptions, ct);
            return result is not null
                ? Result.Ok(result)
                : Result.Fail<EngineVersionsResponse>(new Error
                {
                    Code = "ENGINE_PARSE_ERROR",
                    UserMessage = "解析引擎版本数据失败",
                    TechnicalMessage = "反序列化返回 null",
                    CanRetry = true,
                    Severity = ErrorSeverity.Warning,
                });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error(ex, "引擎版本 API 调用异常");
            return Result.Fail<EngineVersionsResponse>(new Error
            {
                Code = "ENGINE_API_EXCEPTION",
                UserMessage = "引擎版本服务暂不可用",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }
    }

    private async Task InjectAuthHeaderAsync(CancellationToken ct)
    {
        var tokenResult = await _authService.GetAccessTokenAsync(ct);
        if (tokenResult.IsSuccess)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokenResult.Value);
        }
    }

    // === 内部 DTO ===

    internal sealed class EngineVersionsResponse
    {
        public List<EngineVersionDto> Versions { get; set; } = [];
    }

    internal sealed class EngineVersionDto
    {
        public string VersionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long DownloadSize { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
    }
}

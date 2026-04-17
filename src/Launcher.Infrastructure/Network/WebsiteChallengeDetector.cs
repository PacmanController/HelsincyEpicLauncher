// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Infrastructure.Network;

/// <summary>
/// 检测网站前置防护（如 Cloudflare challenge）返回的 HTML 拦截页。
/// 这类响应通常不是业务 API 错误，而是请求尚未到达真实后端。
/// </summary>
internal static class WebsiteChallengeDetector
{
    private static readonly string[] ChallengeMarkers =
    [
        "<title>Just a moment...</title>",
        "Enable JavaScript and cookies to continue",
        "/cdn-cgi/challenge-platform/",
        "window._cf_chl_opt",
    ];

    public static bool IsBlocked(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("cf-mitigated", out var mitigatedValues)
            && mitigatedValues.Any(v => string.Equals(v, "challenge", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return ChallengeMarkers.Any(marker =>
            body.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
// Copyright (c) Helsincy. All rights reserved.

using System.Net;
using System.Text.Json;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Launcher.Shared.Logging;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Epic OAuth token 端点的共享辅助逻辑。
/// </summary>
internal static class EpicTokenEndpoint
{
    internal const string TokenUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";

    internal static void AddClientAuth(HttpRequestMessage request, EpicOAuthOptions options)
    {
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    internal static TokenPair ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString() ?? string.Empty;
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var accountId = root.TryGetProperty("account_id", out var aid) ? aid.GetString() ?? string.Empty : string.Empty;
        var displayName = root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? string.Empty : string.Empty;

        return new TokenPair
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
            AccountId = accountId,
            DisplayName = displayName,
        };
    }

    internal static Error CreateTokenExchangeError(HttpStatusCode statusCode, string body)
    {
        var technicalMessage = $"HTTP {(int)statusCode}: {LogSanitizer.SanitizeHttpBody(body, 400)}";
        if (TryParseProviderError(body, out var providerErrorCode, out var providerError))
        {
            if (string.Equals(providerErrorCode, "errors.com.epicgames.account.oauth.authorization_code_not_found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(providerError, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                return new Error
                {
                    Code = "AUTH_AUTHORIZATION_CODE_EXPIRED",
                    UserMessage = "授权码无效、已过期或已使用。请回到浏览器重新完成登录，并立即粘贴新的 authorizationCode。",
                    TechnicalMessage = technicalMessage,
                    CanRetry = true,
                    Severity = ErrorSeverity.Warning,
                };
            }
        }

        return new Error
        {
            Code = "AUTH_TOKEN_EXCHANGE_FAILED",
            UserMessage = "登录授权失败，请重试",
            TechnicalMessage = technicalMessage,
            CanRetry = true,
            Severity = ErrorSeverity.Error,
        };
    }

    internal static bool TryParseProviderError(string body, out string? errorCode, out string? error)
    {
        errorCode = null;
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            errorCode = root.TryGetProperty("errorCode", out var errorCodeElement)
                ? errorCodeElement.GetString()
                : null;
            error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;
            return !string.IsNullOrWhiteSpace(errorCode) || !string.IsNullOrWhiteSpace(error);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
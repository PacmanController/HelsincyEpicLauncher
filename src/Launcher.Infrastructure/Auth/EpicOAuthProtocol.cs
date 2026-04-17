// Copyright (c) Helsincy. All rights reserved.

using System.Collections.Specialized;
using Launcher.Shared;
using Launcher.Shared.Logging;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Epic OAuth 协议辅助逻辑。负责授权 URL 构建与登录结果归一化。
/// </summary>
internal static class EpicOAuthProtocol
{
    public static string BuildAuthorizeUrl(string authorizeUrl, string clientId, string redirectUri, string state)
    {
        return $"{authorizeUrl}?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}";
    }

    public static string BuildAuthorizationCodeLoginUrl(string clientId)
    {
        var redirectUrl = $"https://www.epicgames.com/id/api/redirect?clientId={Uri.EscapeDataString(clientId)}&responseType=code";
        var encodedRedirectUrl = Uri.EscapeDataString(redirectUrl).Replace("%2F", "/");
        return $"https://www.epicgames.com/id/login?redirectUrl={encodedRedirectUrl}";
    }

    public static Result<EpicLoginResult> NormalizeLoginResult(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return Result.Fail<EpicLoginResult>(CreateError(
                "AUTH_AUTHORIZATION_CODE_INVALID",
                "未识别到有效的授权码，请重试",
                "Authorization code input is empty.",
                canRetry: true,
                severity: ErrorSeverity.Warning));
        }

        var trimmedInput = rawInput.Trim();
        if (trimmedInput.Length > 0 && trimmedInput[0] == '{')
        {
            return Result.Fail<EpicLoginResult>(CreateError(
                "AUTH_AUTHORIZATION_CODE_JSON_NOT_ALLOWED",
                "请不要粘贴完整响应内容；只粘贴 authorizationCode 或完整回调链接。",
                "Manual authorization code input must not contain a full JSON payload.",
                canRetry: true,
                severity: ErrorSeverity.Warning));
        }

        if (trimmedInput.Length >= 2 && trimmedInput[0] == '"' && trimmedInput[^1] == '"')
        {
            trimmedInput = trimmedInput[1..^1].Trim();
        }

        if (TryExtractCodeFromUrl(trimmedInput, out var codeFromUrl))
        {
            return Result.Ok(EpicLoginResult.FromCallbackUrlInput(codeFromUrl!));
        }

        if (string.IsNullOrWhiteSpace(trimmedInput))
        {
            return Result.Fail<EpicLoginResult>(CreateError(
                "AUTH_AUTHORIZATION_CODE_INVALID",
                "未识别到有效的授权码，请重试",
                "Authorization code input is blank after trimming.",
                canRetry: true,
                severity: ErrorSeverity.Warning));
        }

        return Result.Ok(EpicLoginResult.FromAuthorizationCodeInput(trimmedInput));
    }

    public static Result<string> ExtractAuthorizationCode(string rawInput)
    {
        var result = NormalizeLoginResult(rawInput);
        if (!result.IsSuccess)
        {
            return Result.Fail<string>(result.Error!);
        }

        return Result.Ok(result.Value!.Payload);
    }

    public static EpicOAuthCallbackResult ParseCallback(string actualPath, NameValueCollection query, string expectedPath, string expectedState)
    {
        if (!string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return EpicOAuthCallbackResult.Fail(
                CreateError(
                    "AUTH_CALLBACK_PATH_INVALID",
                    "收到无效的授权回调，请重试",
                    $"OAuth callback path mismatch. Expected '{expectedPath}', actual '{actualPath}'.",
                    canRetry: true,
                    severity: ErrorSeverity.Warning),
                "登录失败",
                "收到无效的授权回调路径。请返回应用后重试。");
        }

        var state = query["state"];
        if (string.IsNullOrWhiteSpace(state) || !string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            return EpicOAuthCallbackResult.Fail(
                CreateError(
                    "AUTH_CALLBACK_STATE_INVALID",
                    "授权校验失败，请重试登录",
                    "OAuth callback state is missing or mismatched.",
                    canRetry: true,
                    severity: ErrorSeverity.Warning),
                "登录失败",
                "授权状态校验失败。请返回应用后重新登录。");
        }

        var callbackError = query["error"];
        if (!string.IsNullOrWhiteSpace(callbackError))
        {
            var errorDescription = query["error_description"] ?? string.Empty;
            var userMessage = string.Equals(callbackError, "invalid_redirect_url", StringComparison.OrdinalIgnoreCase)
                ? "当前登录回调地址配置无效，请联系维护者检查 OAuth 配置"
                : "登录授权失败，请重试";

            return EpicOAuthCallbackResult.Fail(
                CreateError(
                    "AUTH_CALLBACK_PROVIDER_ERROR",
                    userMessage,
                    $"OAuth provider returned error '{callbackError}': {LogSanitizer.SanitizeHttpBody(errorDescription, 400)}",
                    canRetry: true,
                    severity: ErrorSeverity.Warning),
                "登录失败",
                string.IsNullOrWhiteSpace(errorDescription)
                    ? "登录授权失败。请返回应用后重试。"
                    : $"登录授权失败：{errorDescription}");
        }

        var code = query["code"];
        if (string.IsNullOrWhiteSpace(code))
        {
            return EpicOAuthCallbackResult.Fail(
                CreateError(
                    "AUTH_CALLBACK_CODE_MISSING",
                    "未收到授权码，登录未完成",
                    "OAuth callback returned without authorization code.",
                    canRetry: true,
                    severity: ErrorSeverity.Warning),
                "登录失败",
                "未收到授权码。请返回应用后重试。");
        }

        return EpicOAuthCallbackResult.Success(code);
    }

    private static Error CreateError(string code, string userMessage, string technicalMessage, bool canRetry, ErrorSeverity severity)
    {
        return new Error
        {
            Code = code,
            UserMessage = userMessage,
            TechnicalMessage = technicalMessage,
            CanRetry = canRetry,
            Severity = severity,
        };
    }

    private static bool TryExtractCodeFromUrl(string rawUrl, out string? code)
    {
        code = null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            var name = Uri.UnescapeDataString(parts[0]);
            if (!string.Equals(name, "code", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "authorizationCode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            code = value;
            return true;
        }

        return false;
    }
}

internal sealed class EpicOAuthCallbackResult
{
    public string? Code { get; private init; }

    public Error? Error { get; private init; }

    public string BrowserTitle { get; private init; } = string.Empty;

    public string BrowserMessage { get; private init; } = string.Empty;

    public bool IsSuccess => Error is null && !string.IsNullOrWhiteSpace(Code);

    public static EpicOAuthCallbackResult Success(string code)
    {
        return new EpicOAuthCallbackResult
        {
            Code = code,
            BrowserTitle = "登录成功！",
            BrowserMessage = "请返回 HelsincyEpicLauncher。此页面可以关闭。",
        };
    }

    public static EpicOAuthCallbackResult Fail(Error error, string browserTitle, string browserMessage)
    {
        return new EpicOAuthCallbackResult
        {
            Error = error,
            BrowserTitle = browserTitle,
            BrowserMessage = browserMessage,
        };
    }
}
// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Auth 模块内部归一化后的登录结果类型。
/// </summary>
internal enum EpicLoginResultKind
{
    AuthorizationCode,
    CallbackUrl,
    ExchangeCode,
    ExternalRefreshToken,
}

/// <summary>
/// Auth 模块内部归一化后的登录结果负载。
/// </summary>
internal sealed record EpicLoginResult
{
    public required EpicLoginResultKind Kind { get; init; }

    public required string Payload { get; init; }

    public required string Source { get; init; }

    public string? RedirectUri { get; init; }

    public bool IncludeTokenType { get; init; } = true;

    public static EpicLoginResult FromAuthorizationCodeInput(string code)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.AuthorizationCode,
            Payload = code,
            Source = "authorization_code_input",
            IncludeTokenType = true,
        };
    }

    public static EpicLoginResult FromJsonAuthorizationCodeInput(string code)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.AuthorizationCode,
            Payload = code,
            Source = "json_authorization_code_input",
            IncludeTokenType = true,
        };
    }

    public static EpicLoginResult FromCallbackUrlInput(string code)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.CallbackUrl,
            Payload = code,
            Source = "callback_url_input",
            IncludeTokenType = true,
        };
    }

    public static EpicLoginResult FromJsonRedirectUrlInput(string code)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.CallbackUrl,
            Payload = code,
            Source = "json_redirect_url_input",
            IncludeTokenType = true,
        };
    }

    public static EpicLoginResult FromLoopbackCallback(string code, string redirectUri)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.AuthorizationCode,
            Payload = code,
            Source = "loopback_callback",
            RedirectUri = redirectUri,
            IncludeTokenType = false,
        };
    }

    public static EpicLoginResult FromExchangeCodeInput(string code)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.ExchangeCode,
            Payload = code,
            Source = "exchange_code_webview",
            IncludeTokenType = true,
        };
    }

    public static EpicLoginResult FromExternalRefreshTokenInput(string refreshToken)
    {
        return new EpicLoginResult
        {
            Kind = EpicLoginResultKind.ExternalRefreshToken,
            Payload = refreshToken,
            Source = "external_refresh_token",
            IncludeTokenType = true,
        };
    }
}
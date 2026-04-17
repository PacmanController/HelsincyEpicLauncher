// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.Configuration;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Epic OAuth 运行时配置。
/// </summary>
internal sealed class EpicOAuthOptions
{
    public const string DefaultRedirectUri = "http://localhost:6780/callback";

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public string RedirectUri { get; init; } = DefaultRedirectUri;

    public TimeSpan CallbackTimeout { get; init; } = TimeSpan.FromMinutes(3);

    public static EpicOAuthOptions FromConfiguration(IConfiguration configuration)
    {
        return new EpicOAuthOptions
        {
            ClientId = configuration["EpicOAuth:ClientId"]
                ?? throw new InvalidOperationException("EpicOAuth:ClientId not configured"),
            ClientSecret = configuration["EpicOAuth:ClientSecret"]
                ?? throw new InvalidOperationException("EpicOAuth:ClientSecret not configured"),
            RedirectUri = string.IsNullOrWhiteSpace(configuration["EpicOAuth:RedirectUri"])
                ? DefaultRedirectUri
                : configuration["EpicOAuth:RedirectUri"]!,
        };
    }
}
// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Auth.Contracts;

/// <summary>
/// 嵌入式 Epic 登录会话描述。由 Auth 提供给 Presentation 承载登录容器。
/// </summary>
public sealed class AuthExchangeCodeLoginContext
{
    public required string LoginUrl { get; init; }

    public string? UserAgent { get; init; }
}
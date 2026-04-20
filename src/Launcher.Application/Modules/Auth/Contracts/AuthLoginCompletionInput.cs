// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Auth.Contracts;

/// <summary>
/// Auth 登录完成输入类型。
/// </summary>
public enum AuthLoginCompletionKind
{
    AuthorizationCode,
    CallbackUrl,
    ExchangeCode,
    ExternalRefreshToken,
}

/// <summary>
/// Auth 登录完成输入。
/// </summary>
public sealed class AuthLoginCompletionInput
{
    public required AuthLoginCompletionKind Kind { get; init; }

    public required string Payload { get; init; }
}
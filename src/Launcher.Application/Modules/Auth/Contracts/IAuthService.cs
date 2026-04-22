// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Auth.Contracts;

/// <summary>
/// Epic Games 认证服务。处理 OAuth 2.0 登录流程和 Token 管理。
/// </summary>
public interface IAuthService
{
    /// <summary>当前是否已认证</summary>
    bool IsAuthenticated { get; }

    /// <summary>当前登录用户信息</summary>
    AuthUserInfo? CurrentUser { get; }

    /// <summary>打开 Epic 登录页，启动 authorization code 登录流程</summary>
    Task<Result> StartAuthorizationCodeLoginAsync(CancellationToken ct = default);

    /// <summary>准备嵌入式 exchange code 登录上下文</summary>
    Task<Result<AuthExchangeCodeLoginContext>> StartExchangeCodeLoginAsync(CancellationToken ct = default);

    /// <summary>提交 authorization code、完整回调链接，或浏览器返回的 JSON 响应，完成登录</summary>
    Task<Result<AuthUserInfo>> CompleteAuthorizationCodeLoginAsync(string authorizationCodeOrCallbackUrl, CancellationToken ct = default);

    /// <summary>提交类型化登录结果，完成登录</summary>
    Task<Result<AuthUserInfo>> CompleteLoginAsync(AuthLoginCompletionInput input, CancellationToken ct = default);

    /// <summary>登出</summary>
    Task<Result> LogoutAsync(CancellationToken ct = default);

    /// <summary>获取有效的 Access Token（自动刷新过期 Token）</summary>
    Task<Result<string>> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>尝试从缓存恢复会话（启动时调用）</summary>
    Task<Result<AuthUserInfo>> TryRestoreSessionAsync(CancellationToken ct = default);

    /// <summary>会话已认证事件（登录成功或恢复成功）</summary>
    event Action<AuthUserInfo>? SessionAuthenticated;

    /// <summary>会话过期事件</summary>
    event Action<SessionExpiredEvent>? SessionExpired;
}

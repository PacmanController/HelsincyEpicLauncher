// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// 认证服务实现。协调 OAuth 流程、Token 存储和会话管理。
/// </summary>
internal sealed class AuthService : IAuthService, IDisposable
{
    private readonly ILogger _logger = Log.ForContext<AuthService>();
    private readonly EpicOAuthHandler _oauthHandler;
    private readonly ITokenStore _tokenStore;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _restoreLock = new(1, 1);

    private TokenPair? _currentTokens;
    private AuthUserInfo? _currentUser;

    public bool IsAuthenticated
    {
        get
        {
            lock (_lock)
            {
                return _currentTokens is not null && _currentTokens.ExpiresAt > DateTime.UtcNow;
            }
        }
    }

    public AuthUserInfo? CurrentUser
    {
        get
        {
            lock (_lock)
            {
                return _currentUser;
            }
        }
    }

    public event Action<AuthUserInfo>? SessionAuthenticated;
    public event Action<SessionExpiredEvent>? SessionExpired;

    public AuthService(EpicOAuthHandler oauthHandler, ITokenStore tokenStore)
    {
        _oauthHandler = oauthHandler;
        _tokenStore = tokenStore;
    }

    public Task<Result> StartAuthorizationCodeLoginAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _logger.Information("开始 authorization code 登录流程");
        return Task.FromResult(_oauthHandler.StartAuthorizationCodeLogin());
    }

    public async Task<Result<AuthUserInfo>> CompleteAuthorizationCodeLoginAsync(string authorizationCodeOrCallbackUrl, CancellationToken ct = default)
    {
        _logger.Information("提交 authorization code/回调链接，完成登录流程");

        var tokenResult = await _oauthHandler.ExchangeAuthorizationCodeAsync(authorizationCodeOrCallbackUrl, ct);
        if (!tokenResult.IsSuccess)
        {
            return Result.Fail<AuthUserInfo>(tokenResult.Error!);
        }

        var tokens = tokenResult.Value!;

        var userInfo = await ResolveUserInfoAsync(tokens, ct);

        await _tokenStore.SaveTokensAsync(tokens, ct);

        lock (_lock)
        {
            _currentTokens = tokens;
            _currentUser = userInfo;
        }

        _logger.Information("登录成功 | AccountId={AccountId} | DisplayName={Name}",
            userInfo.AccountId, userInfo.DisplayName);
        SessionAuthenticated?.Invoke(userInfo);
        return Result.Ok(userInfo);
    }

    public async Task<Result> LogoutAsync(CancellationToken ct = default)
    {
        _logger.Information("开始登出流程");

        TokenPair? tokens;
        lock (_lock)
        {
            tokens = _currentTokens;
        }

        // 1. 撤销远程 Token（最佳努力）
        if (tokens is not null)
        {
            await _oauthHandler.RevokeTokenAsync(tokens.AccessToken, ct);
        }

        // 2. 清除本地存储
        await _tokenStore.ClearAsync(ct);

        // 3. 清除内存状态
        lock (_lock)
        {
            _currentTokens = null;
            _currentUser = null;
        }

        // 4. 发布会话过期事件
        SessionExpired?.Invoke(new SessionExpiredEvent("用户主动登出"));

        _logger.Information("登出完成");
        return Result.Ok();
    }

    public async Task<Result<string>> GetAccessTokenAsync(CancellationToken ct = default)
    {
        TokenPair? tokens;
        lock (_lock)
        {
            tokens = _currentTokens;
        }

        if (tokens is null)
        {
            return Result.Fail<string>(new Error
            {
                Code = "AUTH_NOT_AUTHENTICATED",
                UserMessage = "未登录，请先登录",
                TechnicalMessage = "No token pair available",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        // 检查是否需要刷新（提前 5 分钟）
        if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
        {
            return Result.Ok(tokens.AccessToken);
        }

        // 需要刷新 — 用 SemaphoreSlim 防止并发刷新竞态
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Double-check: 另一个线程可能已经刷新完成
            lock (_lock)
            {
                tokens = _currentTokens;
            }

            if (tokens is null)
            {
                return Result.Fail<string>(new Error
                {
                    Code = "AUTH_NOT_AUTHENTICATED",
                    UserMessage = "未登录，请先登录",
                    TechnicalMessage = "Session cleared during refresh",
                    CanRetry = false,
                    Severity = ErrorSeverity.Warning,
                });
            }

            if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                return Result.Ok(tokens.AccessToken);
            }

            _logger.Debug("Access Token 即将过期，主动刷新");
            var refreshResult = await _oauthHandler.RefreshTokenAsync(tokens.RefreshToken, ct);

            if (!refreshResult.IsSuccess)
            {
                // 刷新失败 → 会话过期
                lock (_lock)
                {
                    _currentTokens = null;
                    _currentUser = null;
                }

                await _tokenStore.ClearAsync(ct);
                SessionExpired?.Invoke(new SessionExpiredEvent("Token 刷新失败"));

                return Result.Fail<string>(refreshResult.Error!);
            }

            var newTokens = refreshResult.Value!;
            await _tokenStore.SaveTokensAsync(newTokens, ct);

            // 写回前检查：如果 Logout 已介入清空了 token，则不写回
            lock (_lock)
            {
                if (_currentTokens is not null)
                {
                    _currentTokens = newTokens;
                }
                else
                {
                    _logger.Warning("刷新期间检测到登出操作，丢弃刷新结果");
                    return Result.Fail<string>(new Error
                    {
                        Code = "AUTH_LOGGED_OUT_DURING_REFRESH",
                        UserMessage = "操作期间已登出",
                        TechnicalMessage = "Logout occurred during token refresh",
                        CanRetry = false,
                        Severity = ErrorSeverity.Warning,
                    });
                }
            }

            return Result.Ok(newTokens.AccessToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<Result<AuthUserInfo>> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        _logger.Debug("尝试恢复会话");

        lock (_lock)
        {
            if (_currentTokens is not null
                && _currentUser is not null
                && _currentTokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                _logger.Debug("当前内存会话仍有效，跳过恢复");
                return Result.Ok(_currentUser);
            }
        }

        await _restoreLock.WaitAsync(ct);
        try
        {
            lock (_lock)
            {
                if (_currentTokens is not null
                    && _currentUser is not null
                    && _currentTokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
                {
                    _logger.Debug("恢复期间检测到会话已就绪，跳过重复恢复");
                    return Result.Ok(_currentUser);
                }
            }

            // 1. 从存储加载 Token
            var tokens = await _tokenStore.LoadTokensAsync(ct);
            if (tokens is null)
            {
                _logger.Debug("未找到缓存的 Token");
                return Result.Fail<AuthUserInfo>(new Error
                {
                    Code = "AUTH_NO_CACHED_SESSION",
                    UserMessage = "无缓存会话",
                    TechnicalMessage = "No tokens found in store",
                    CanRetry = false,
                    Severity = ErrorSeverity.Warning,
                });
            }

            // 2. 检查 access_token 是否还有效
            if (tokens.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            {
                // Token 仍有效，直接恢复
                return await RestoreWithTokens(tokens, ct);
            }

            // 3. Token 已过期，尝试刷新
            if (string.IsNullOrEmpty(tokens.RefreshToken))
            {
                await _tokenStore.ClearAsync(ct);
                return Result.Fail<AuthUserInfo>(new Error
                {
                    Code = "AUTH_SESSION_EXPIRED",
                    UserMessage = "会话已过期，请重新登录",
                    TechnicalMessage = "Access token expired and no refresh token available",
                    CanRetry = false,
                    Severity = ErrorSeverity.Warning,
                });
            }

            _logger.Debug("缓存 Token 已过期，尝试刷新");
            var refreshResult = await _oauthHandler.RefreshTokenAsync(tokens.RefreshToken, ct);

            if (!refreshResult.IsSuccess)
            {
                await _tokenStore.ClearAsync(ct);
                _logger.Warning("会话恢复失败：Token 刷新失败");
                return Result.Fail<AuthUserInfo>(new Error
                {
                    Code = "AUTH_RESTORE_FAILED",
                    UserMessage = "会话恢复失败，请重新登录",
                    TechnicalMessage = "Token refresh failed during session restore",
                    CanRetry = false,
                    Severity = ErrorSeverity.Warning,
                });
            }

            var newTokens = refreshResult.Value!;
            await _tokenStore.SaveTokensAsync(newTokens, ct);

            return await RestoreWithTokens(newTokens, ct);
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    private async Task<Result<AuthUserInfo>> RestoreWithTokens(TokenPair tokens, CancellationToken ct)
    {
        var userInfo = await ResolveUserInfoAsync(tokens, ct);

        lock (_lock)
        {
            _currentTokens = tokens;
            _currentUser = userInfo;
        }

        _logger.Information("会话已恢复 | AccountId={AccountId} | DisplayName={Name}",
            userInfo.AccountId, userInfo.DisplayName);
        SessionAuthenticated?.Invoke(userInfo);
        return Result.Ok(userInfo);
    }

    private async Task<AuthUserInfo> ResolveUserInfoAsync(TokenPair tokens, CancellationToken ct)
    {
        var userResult = await _oauthHandler.GetAccountInfoAsync(tokens.AccessToken, tokens.AccountId, ct);
        if (userResult.IsSuccess)
        {
            return userResult.Value!;
        }

        _logger.Warning("获取详细用户信息失败，使用 Token 中的基本信息");
        return new AuthUserInfo
        {
            AccountId = tokens.AccountId,
            DisplayName = tokens.DisplayName,
            Email = string.Empty,
        };
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        _restoreLock.Dispose();
    }
}

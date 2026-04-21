// Copyright (c) Helsincy. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Launcher.Shared.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Epic Games OAuth 2.0 处理器。
/// 管理本地 HTTP 回调监听、授权码交换、Token 刷新。
/// </summary>
internal sealed class EpicOAuthHandler
{
    private readonly ILogger _logger = Log.ForContext<EpicOAuthHandler>();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EpicOAuthOptions _options;
    private readonly IEpicLoginGrantExecutor[] _grantExecutors;

    // Epic Games OAuth 端点
    private const string AuthorizeUrl = "https://www.epicgames.com/id/authorize";
    private const string AccountInfoUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/public/account";
    private const string RevokeUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/sessions/kill";

    public EpicOAuthHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _options = EpicOAuthOptions.FromConfiguration(configuration);
        _grantExecutors =
        [
            new AuthorizationCodeGrantExecutor(httpClientFactory, _options),
            new ExchangeCodeGrantExecutor(httpClientFactory, _options),
        ];
    }

    /// <summary>
    /// 启动 OAuth 授权流程。打开系统浏览器并等待回调。
    /// </summary>
    public async Task<Result<TokenPair>> AuthorizeAsync(CancellationToken ct)
    {
        // 1. 启动本地 HTTP 监听器
        var listenerResult = StartListener(_options.RedirectUri);
        if (!listenerResult.IsSuccess)
        {
            return Result.Fail<TokenPair>(listenerResult.Error!);
        }

        var (listener, redirectUri) = listenerResult.Value!;
        _logger.Information("OAuth 回调监听已启动 | RedirectUri={Uri}", redirectUri.AbsoluteUri);

        try
        {
            var state = Guid.NewGuid().ToString("N");

            // 2. 构建授权 URL 并打开浏览器
            var authUrl = EpicOAuthProtocol.BuildAuthorizeUrl(AuthorizeUrl, _options.ClientId, redirectUri.AbsoluteUri, state);
            OpenBrowser(authUrl);
            _logger.Debug("已打开浏览器进行 OAuth 授权 | Url={Url}", LogSanitizer.SanitizeUrl(authUrl));

            // 3. 等待回调获取 authorization_code
            var callbackResult = await WaitForCallbackAsync(listener, redirectUri, state, ct);
            if (!callbackResult.IsSuccess)
            {
                return Result.Fail<TokenPair>(callbackResult.Error!);
            }

            _logger.Debug("已收到授权码");

            // 4. 用授权码换取 Token
            return await ExecuteLoginResultAsync(
                EpicLoginResult.FromLoopbackCallback(callbackResult.Code!, redirectUri.AbsoluteUri),
                ct);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    public Result StartAuthorizationCodeLogin()
    {
        try
        {
            var loginUrl = EpicOAuthProtocol.BuildAuthorizationCodeLoginUrl(_options.ClientId);
            OpenBrowser(loginUrl);
            _logger.Information("已打开 authorization code 登录页面 | Url={Url}", LogSanitizer.SanitizeUrl(loginUrl));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "打开 authorization code 登录页面失败");
            return Result.Fail(new Error
            {
                Code = "AUTH_LOGIN_PAGE_OPEN_FAILED",
                UserMessage = "无法打开 Epic 登录页面，请重试",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    public Result<AuthExchangeCodeLoginContext> StartExchangeCodeLogin()
    {
        try
        {
            return Result.Ok(new AuthExchangeCodeLoginContext
            {
                LoginUrl = EpicOAuthProtocol.BuildEmbeddedExchangeCodeLoginUrl(),
                UserAgent = _options.EmbeddedLoginUserAgent,
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "准备 exchange code 登录上下文失败");
            return Result.Fail<AuthExchangeCodeLoginContext>(new Error
            {
                Code = "AUTH_EXCHANGE_LOGIN_START_FAILED",
                UserMessage = "无法准备嵌入式登录，请重试",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    public async Task<Result<TokenPair>> CompleteInteractiveLoginAsync(string loginResultPayload, CancellationToken ct)
    {
        var normalizedResult = EpicOAuthProtocol.NormalizeLoginResult(loginResultPayload);
        if (!normalizedResult.IsSuccess)
        {
            return Result.Fail<TokenPair>(normalizedResult.Error!);
        }

        return await ExecuteLoginResultAsync(normalizedResult.Value!, ct);
    }

    public async Task<Result<TokenPair>> CompleteLoginAsync(AuthLoginCompletionInput input, CancellationToken ct)
    {
        if (input is null)
        {
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_LOGIN_INPUT_MISSING",
                UserMessage = "未识别到有效的登录结果，请重试",
                TechnicalMessage = "Typed login completion input is null.",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }

        if (string.IsNullOrWhiteSpace(input.Payload))
        {
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_LOGIN_INPUT_EMPTY",
                UserMessage = "未识别到有效的登录结果，请重试",
                TechnicalMessage = $"Typed login completion payload is empty for kind '{input.Kind}'.",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }

        EpicLoginResult normalizedResult = input.Kind switch
        {
            AuthLoginCompletionKind.AuthorizationCode => EpicLoginResult.FromAuthorizationCodeInput(input.Payload.Trim()),
            AuthLoginCompletionKind.CallbackUrl => EpicLoginResult.FromCallbackUrlInput(input.Payload.Trim()),
            AuthLoginCompletionKind.ExchangeCode => EpicLoginResult.FromExchangeCodeInput(input.Payload.Trim()),
            AuthLoginCompletionKind.ExternalRefreshToken => EpicLoginResult.FromExternalRefreshTokenInput(input.Payload.Trim()),
            _ => null!,
        };

        if (normalizedResult is null)
        {
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_LOGIN_INPUT_KIND_UNSUPPORTED",
                UserMessage = "当前登录结果类型暂不支持",
                TechnicalMessage = $"Unsupported typed login completion kind '{input.Kind}'.",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        return await ExecuteLoginResultAsync(normalizedResult, ct);
    }

    /// <summary>
    /// 用 refresh_token 刷新 access_token
    /// </summary>
    public async Task<Result<TokenPair>> RefreshTokenAsync(string refreshToken, CancellationToken ct)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["token_type"] = "eg1",
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = content,
            };
            EpicTokenEndpoint.AddClientAuth(request, _options);

            using var httpClient = _httpClientFactory.CreateClient("EpicAuth");
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("Token 刷新失败 | StatusCode={Code} | Body={Body}", response.StatusCode, LogSanitizer.SanitizeHttpBody(body, 400));
                return Result.Fail<TokenPair>(new Error
                {
                    Code = "AUTH_REFRESH_FAILED",
                    UserMessage = "Token 刷新失败，请重新登录",
                    TechnicalMessage = $"HTTP {(int)response.StatusCode}: {LogSanitizer.SanitizeHttpBody(body)}",
                    CanRetry = false,
                    Severity = ErrorSeverity.Warning,
                });
            }

            var tokenPair = EpicTokenEndpoint.ParseTokenResponse(body);
            _logger.Information("Token 已刷新 | ExpiresAt={ExpiresAt}", tokenPair.ExpiresAt);
            return Result.Ok(tokenPair);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Token 刷新异常");
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_REFRESH_EXCEPTION",
                UserMessage = "Token 刷新过程中出错",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    /// <summary>
    /// 获取用户账户信息
    /// </summary>
    public async Task<Result<AuthUserInfo>> GetAccountInfoAsync(string accessToken, string accountId, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AccountInfoUrl}/{accountId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var httpClient = _httpClientFactory.CreateClient("EpicAuth");
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("获取账户信息失败 | StatusCode={Code} | Body={Body}", response.StatusCode, LogSanitizer.SanitizeHttpBody(body, 400));
                return Result.Fail<AuthUserInfo>(new Error
                {
                    Code = "AUTH_ACCOUNT_INFO_FAILED",
                    UserMessage = "获取账户信息失败",
                    TechnicalMessage = $"HTTP {(int)response.StatusCode}: {LogSanitizer.SanitizeHttpBody(body)}",
                    CanRetry = true,
                    Severity = ErrorSeverity.Warning,
                });
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var userInfo = new AuthUserInfo
            {
                AccountId = root.GetProperty("id").GetString() ?? string.Empty,
                DisplayName = root.GetProperty("displayName").GetString() ?? string.Empty,
                Email = root.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
            };

            return Result.Ok(userInfo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "获取账户信息异常");
            return Result.Fail<AuthUserInfo>(new Error
            {
                Code = "AUTH_ACCOUNT_INFO_EXCEPTION",
                UserMessage = "获取账户信息过程中出错",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    /// <summary>
    /// 撤销 Token
    /// </summary>
    public async Task<Result> RevokeTokenAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{RevokeUrl}/{accessToken}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using var httpClient = _httpClientFactory.CreateClient("EpicAuth");
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("Token 撤销失败 | StatusCode={Code}", response.StatusCode);
            }
            else
            {
                _logger.Information("Token 已撤销");
            }

            // 即使撤销失败，登出流程仍应继续
            return Result.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Warning(ex, "Token 撤销异常（将继续登出流程）");
            return Result.Ok();
        }
    }

    private async Task<Result<TokenPair>> ExecuteLoginResultAsync(EpicLoginResult loginResult, CancellationToken ct)
    {
        _logger.Information(
            "Auth 登录结果已归一 | Kind={Kind} | Source={Source} | IncludeTokenType={IncludeTokenType} | HasRedirectUri={HasRedirectUri}",
            loginResult.Kind,
            loginResult.Source,
            loginResult.IncludeTokenType,
            !string.IsNullOrWhiteSpace(loginResult.RedirectUri));

        foreach (var executor in _grantExecutors)
        {
            if (!executor.CanExecute(loginResult.Kind))
            {
                continue;
            }

            _logger.Information(
                "Auth grant 执行器已匹配 | Kind={Kind} | Source={Source} | GrantType={GrantType}",
                loginResult.Kind,
                loginResult.Source,
                executor.GrantType);
            return await executor.ExecuteAsync(loginResult, ct);
        }

        _logger.Warning(
            "未找到可处理的 Auth 登录结果执行器 | Kind={Kind} | Source={Source}",
            loginResult.Kind,
            loginResult.Source);
        return Result.Fail<TokenPair>(new Error
        {
            Code = "AUTH_LOGIN_RESULT_UNSUPPORTED",
            UserMessage = "当前登录结果类型暂不支持",
            TechnicalMessage = $"No login grant executor can handle kind '{loginResult.Kind}' from source '{loginResult.Source}'.",
            CanRetry = false,
            Severity = ErrorSeverity.Error,
        });
    }

    private const string TokenUrl = EpicTokenEndpoint.TokenUrl;

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private static Result<(HttpListener Listener, Uri RedirectUri)> StartListener(string redirectUriText)
    {
        if (!Uri.TryCreate(redirectUriText, UriKind.Absolute, out var redirectUri))
        {
            return Result.Fail<(HttpListener Listener, Uri RedirectUri)>(new Error
            {
                Code = "AUTH_REDIRECT_CONFIG_INVALID",
                UserMessage = "OAuth 回调地址配置无效",
                TechnicalMessage = $"Invalid redirect URI configuration: {redirectUriText}",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        if (!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || !redirectUri.IsLoopback)
        {
            return Result.Fail<(HttpListener Listener, Uri RedirectUri)>(new Error
            {
                Code = "AUTH_REDIRECT_UNSUPPORTED",
                UserMessage = "当前版本仅支持本地回调地址登录",
                TechnicalMessage = $"Unsupported redirect URI for current loopback listener strategy: {redirectUri}",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        if (redirectUri.IsDefaultPort)
        {
            return Result.Fail<(HttpListener Listener, Uri RedirectUri)>(new Error
            {
                Code = "AUTH_REDIRECT_PORT_MISSING",
                UserMessage = "OAuth 回调地址缺少端口配置",
                TechnicalMessage = $"Redirect URI must use an explicit loopback port: {redirectUri}",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        try
        {
            var listener = new HttpListener();
            var prefix = redirectUri.GetLeftPart(UriPartial.Authority);
            if (!prefix.EndsWith('/'))
            {
                prefix += "/";
            }

            listener.Prefixes.Add(prefix);
            listener.Start();

            return Result.Ok((listener, redirectUri));
        }
        catch (HttpListenerException ex)
        {
            return Result.Fail<(HttpListener Listener, Uri RedirectUri)>(new Error
            {
                Code = "AUTH_REDIRECT_LISTENER_FAILED",
                UserMessage = "无法启动本地登录回调监听，请检查端口占用后重试",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
                InnerException = ex,
            });
        }
    }

    private async Task<EpicOAuthCallbackResult> WaitForCallbackAsync(HttpListener listener, Uri redirectUri, string expectedState, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.CallbackTimeout);

        try
        {
            var context = await listener.GetContextAsync().WaitAsync(cts.Token);
            var request = context.Request;
            var callbackResult = EpicOAuthProtocol.ParseCallback(request.Url?.AbsolutePath ?? string.Empty, request.QueryString, redirectUri.AbsolutePath, expectedState);

            var responseHtml = BuildBrowserResponse(callbackResult);
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes, cts.Token);
            context.Response.Close();

            return callbackResult;
        }
        catch (OperationCanceledException)
        {
            return EpicOAuthCallbackResult.Fail(new Error
            {
                Code = "AUTH_CALLBACK_TIMED_OUT",
                UserMessage = "登录超时，未收到授权回调",
                TechnicalMessage = $"No OAuth callback received within {_options.CallbackTimeout.TotalMinutes:F0} minutes.",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            }, "登录超时", "未收到授权回调。请返回应用后重试。");
        }
    }

    private static string BuildBrowserResponse(EpicOAuthCallbackResult result)
    {
        return $"<html><body><h1>{WebUtility.HtmlEncode(result.BrowserTitle)}</h1><p>{WebUtility.HtmlEncode(result.BrowserMessage)}</p></body></html>";
    }
}

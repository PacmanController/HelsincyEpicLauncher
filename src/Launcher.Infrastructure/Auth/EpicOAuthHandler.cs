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

    // Epic Games OAuth 端点
    private const string AuthorizeUrl = "https://www.epicgames.com/id/authorize";
    private const string TokenUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
    private const string AccountInfoUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/public/account";
    private const string RevokeUrl = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/sessions/kill";

    public EpicOAuthHandler(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _options = EpicOAuthOptions.FromConfiguration(configuration);
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
            return await ExchangeCodeAsync(callbackResult.Code!, redirectUri.AbsoluteUri, includeTokenType: false, ct);
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

    public async Task<Result<TokenPair>> ExchangeAuthorizationCodeAsync(string authorizationCodeOrJson, CancellationToken ct)
    {
        var codeResult = EpicOAuthProtocol.ExtractAuthorizationCode(authorizationCodeOrJson);
        if (!codeResult.IsSuccess)
        {
            return Result.Fail<TokenPair>(codeResult.Error!);
        }

        _logger.Information("开始使用 authorization code 完成登录");
        return await ExchangeCodeAsync(codeResult.Value!, redirectUri: null, includeTokenType: true, ct);
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
            AddClientAuth(request);

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

            var tokenPair = ParseTokenResponse(body);
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

    /// <summary>
    /// 用授权码换取 Token
    /// </summary>
    private async Task<Result<TokenPair>> ExchangeCodeAsync(string code, string? redirectUri, bool includeTokenType, CancellationToken ct)
    {
        try
        {
            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
            };

            if (!string.IsNullOrWhiteSpace(redirectUri))
            {
                parameters["redirect_uri"] = redirectUri;
            }

            if (includeTokenType)
            {
                parameters["token_type"] = "eg1";
            }

            var content = new FormUrlEncodedContent(parameters);

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = content,
            };
            AddClientAuth(request);

            using var httpClient = _httpClientFactory.CreateClient("EpicAuth");
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("Token 交换失败 | StatusCode={Code} | Body={Body}", response.StatusCode, LogSanitizer.SanitizeHttpBody(body, 400));
                return Result.Fail<TokenPair>(CreateTokenExchangeError(response.StatusCode, body));
            }

            var tokenPair = ParseTokenResponse(body);
            _logger.Information("Token 交换成功 | ExpiresAt={ExpiresAt}", tokenPair.ExpiresAt);
            return Result.Ok(tokenPair);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(ex, "Token 交换异常");
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_TOKEN_EXCHANGE_EXCEPTION",
                UserMessage = "登录过程中出错，请重试",
                TechnicalMessage = LogSanitizer.SanitizeHttpBody(ex.Message),
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    private static TokenPair ParseTokenResponse(string json)
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

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private static Error CreateTokenExchangeError(HttpStatusCode statusCode, string body)
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

    private void AddClientAuth(HttpRequestMessage request)
    {
        var credentials = Convert.ToBase64String(
            System.Text.Encoding.ASCII.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    private static bool TryParseProviderError(string body, out string? errorCode, out string? error)
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

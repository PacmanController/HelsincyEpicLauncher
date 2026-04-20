// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Launcher.Shared.Logging;
using Serilog;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// 执行 exchange_code grant 的内部执行器。
/// </summary>
internal sealed class ExchangeCodeGrantExecutor : IEpicLoginGrantExecutor
{
    private readonly ILogger _logger = Log.ForContext<ExchangeCodeGrantExecutor>();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EpicOAuthOptions _options;

    public ExchangeCodeGrantExecutor(IHttpClientFactory httpClientFactory, EpicOAuthOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string GrantType => "exchange_code";

    public bool CanExecute(EpicLoginResultKind kind)
    {
        return kind == EpicLoginResultKind.ExchangeCode;
    }

    public async Task<Result<TokenPair>> ExecuteAsync(EpicLoginResult input, CancellationToken ct)
    {
        if (!CanExecute(input.Kind))
        {
            return Result.Fail<TokenPair>(new Error
            {
                Code = "AUTH_LOGIN_RESULT_KIND_UNSUPPORTED",
                UserMessage = "当前登录结果类型暂不支持",
                TechnicalMessage = $"ExchangeCodeGrantExecutor cannot handle login result kind '{input.Kind}'.",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            });
        }

        try
        {
            _logger.Information(
                "Auth token exchange started | GrantType={GrantType} | Kind={Kind} | Source={Source}",
                GrantType,
                input.Kind,
                input.Source);

            var parameters = new Dictionary<string, string>
            {
                ["grant_type"] = GrantType,
                ["exchange_code"] = input.Payload,
                ["token_type"] = "eg1",
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, EpicTokenEndpoint.TokenUrl)
            {
                Content = new FormUrlEncodedContent(parameters),
            };
            EpicTokenEndpoint.AddClientAuth(request, _options);

            using var httpClient = _httpClientFactory.CreateClient("EpicAuth");
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                EpicTokenEndpoint.TryParseProviderError(body, out var providerErrorCode, out var providerError);
                _logger.Warning(
                    "Auth token exchange failed | GrantType={GrantType} | Kind={Kind} | Source={Source} | StatusCode={StatusCode} | ProviderErrorCode={ProviderErrorCode} | ProviderError={ProviderError} | Body={Body}",
                    GrantType,
                    input.Kind,
                    input.Source,
                    response.StatusCode,
                    providerErrorCode,
                    providerError,
                    LogSanitizer.SanitizeHttpBody(body, 400));
                return Result.Fail<TokenPair>(EpicTokenEndpoint.CreateTokenExchangeError(response.StatusCode, body, GrantType));
            }

            var tokenPair = EpicTokenEndpoint.ParseTokenResponse(body);
            _logger.Information(
                "Auth token exchange succeeded | GrantType={GrantType} | Kind={Kind} | Source={Source} | ExpiresAt={ExpiresAt}",
                GrantType,
                input.Kind,
                input.Source,
                tokenPair.ExpiresAt);
            return Result.Ok(tokenPair);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error(
                ex,
                "Auth token exchange exception | GrantType={GrantType} | Kind={Kind} | Source={Source}",
                GrantType,
                input.Kind,
                input.Source);
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
}
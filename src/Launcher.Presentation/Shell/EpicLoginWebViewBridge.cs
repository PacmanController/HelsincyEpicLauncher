// Copyright (c) Helsincy. All rights reserved.

using System.Text.Json;

namespace Launcher.Presentation.Shell;

/// <summary>
/// Epic 嵌入式登录页与宿主 WebView2 之间的最小桥接辅助。
/// </summary>
internal static class EpicLoginWebViewBridge
{
    private static readonly string[] AllowedEpicHosts =
    [
        "epicgames.com",
        "www.epicgames.com",
        "accounts.epicgames.com",
    ];

    internal const string BootstrapScript = """
        (() => {
            const post = (payload) => {
                try {
                    window.chrome.webview.postMessage(JSON.stringify(payload));
                } catch {
                }
            };

            window.ue = window.ue || {};
            window.ue.signinprompt = window.ue.signinprompt || {};
            window.ue.signinprompt.requestexchangecodesignin = function(exchangeCode, param) {
                post({
                    type: 'exchange_code',
                    exchangeCode: typeof exchangeCode === 'string' ? exchangeCode : '',
                    param: param == null ? null : String(param)
                });
            };
            window.ue.signinprompt.registersignincompletecallback = function() {
                post({ type: 'signin_complete_callback_registered' });
            };

            window.ue.common = window.ue.common || {};
            window.ue.common.launchexternalurl = function(url) {
                post({
                    type: 'launch_external_url',
                    url: typeof url === 'string' ? url : ''
                });
            };
        })();
        """;

    public static bool IsTrustedEpicUri(Uri? uri)
    {
        if (uri is null)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowedEpicHosts.Any(host =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTrustedExternalLaunchUri(Uri? uri)
    {
        return IsTrustedEpicUri(uri);
    }

    public static bool TryParseMessage(string rawMessage, out EpicLoginWebViewMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawMessage);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            message = new EpicLoginWebViewMessage
            {
                Type = type,
                ExchangeCode = root.TryGetProperty("exchangeCode", out var exchangeCodeElement)
                    ? exchangeCodeElement.GetString()
                    : null,
                Url = root.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString()
                    : null,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed class EpicLoginWebViewMessage
{
    public required string Type { get; init; }

    public string? ExchangeCode { get; init; }

    public string? Url { get; init; }
}
// Copyright (c) Helsincy. All rights reserved.

using System.Text.RegularExpressions;

namespace Launcher.Shared.Logging;

/// <summary>
/// 日志字段脱敏工具。防止敏感信息写入日志。
/// </summary>
public static partial class LogSanitizer
{
    /// <summary>
    /// 脱敏 token：保留前 4 位 + ... + 后 4 位
    /// </summary>
    public static string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length < 12)
            return "***";
        return $"{token[..4]}...{token[^4..]}";
    }

    /// <summary>
    /// 脱敏 URL：移除 query string 中的敏感参数值
    /// </summary>
    public static string SanitizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        return SensitiveParamRegex().Replace(url, "$1***");
    }

    [GeneratedRegex(@"((?:token|code|key|secret|password)=)[^&]*", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveParamRegex();

    /// <summary>
    /// 脱敏 HTTP 响应体：移除敏感 JSON 字段值，截断过长内容
    /// </summary>
    public static string SanitizeHttpBody(string body, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;

        var sanitized = SensitiveJsonFieldRegex().Replace(body, "$1\"***\"");
        return sanitized.Length > maxLength
            ? $"{sanitized[..maxLength]}...[truncated]"
            : sanitized;
    }

    [GeneratedRegex(@"(""(?:access_token|refresh_token|token|code|secret|password|authorization)""\s*:\s*)""[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveJsonFieldRegex();
}

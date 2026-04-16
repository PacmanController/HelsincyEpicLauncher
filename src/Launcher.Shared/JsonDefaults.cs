// Copyright (c) Helsincy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher.Shared;

/// <summary>
/// 集中管理的 JsonSerializerOptions 实例。避免各服务重复定义。
/// </summary>
public static class JsonDefaults
{
    /// <summary>
    /// API 响应反序列化（snake_case 命名、忽略 null）
    /// </summary>
    public static JsonSerializerOptions SnakeCaseLower { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// 配置文件序列化（camelCase 命名、格式化输出）
    /// </summary>
    public static JsonSerializerOptions CamelCaseIndented { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

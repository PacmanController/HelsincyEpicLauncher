// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Plugins.Contracts;

/// <summary>已安装插件摘要</summary>
public sealed class PluginSummary
{
    public required string PluginId { get; init; }
    public required string Name { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public required string InstallPath { get; init; }
    public IReadOnlyList<string> SupportedEngineVersions { get; init; } = [];
    public bool IsEnabled { get; init; }
}

/// <summary>插件兼容性检查报告</summary>
public sealed class CompatibilityReport
{
    public bool IsCompatible { get; init; }
    public string? IncompatibilityReason { get; init; }
}

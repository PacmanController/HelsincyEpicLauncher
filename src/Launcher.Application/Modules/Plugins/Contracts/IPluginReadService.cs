// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Plugins.Contracts;

/// <summary>
/// 插件查询服务。
/// </summary>
public interface IPluginReadService
{
    /// <summary>获取已安装的插件列表</summary>
    Task<Result<IReadOnlyList<PluginSummary>>> GetInstalledPluginsAsync(CancellationToken ct);

    /// <summary>检查插件与引擎版本的兼容性</summary>
    Task<Result<CompatibilityReport>> CheckCompatibilityAsync(
        string pluginId, string engineVersionId, CancellationToken ct);
}

// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Plugins.Contracts;

/// <summary>
/// 插件操作服务。
/// </summary>
public interface IPluginCommandService
{
    /// <summary>将插件添加到指定 UE 项目</summary>
    Task<Result> AddToProjectAsync(string pluginId, string projectPath, CancellationToken ct);

    /// <summary>从 UE 项目中移除插件</summary>
    Task<Result> RemoveFromProjectAsync(string pluginId, string projectPath, CancellationToken ct);
}

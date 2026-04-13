// Copyright (c) Helsincy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Application.Modules.Plugins.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Plugins;

/// <summary>
/// 插件操作服务。通过编辑 .uproject 文件管理项目插件引用。
/// </summary>
public sealed class PluginCommandService : IPluginCommandService
{
    private static readonly ILogger Logger = Log.ForContext<PluginCommandService>();

    private readonly IPluginReadService _pluginReadService;

    public PluginCommandService(IPluginReadService pluginReadService)
    {
        _pluginReadService = pluginReadService;
    }

    public async Task<Result> AddToProjectAsync(string pluginId, string projectPath, CancellationToken ct)
    {
        Logger.Information("添加插件 {PluginId} 到项目 {Project}", pluginId, projectPath);

        // 查找插件信息
        var pluginsResult = await _pluginReadService.GetInstalledPluginsAsync(ct);
        if (!pluginsResult.IsSuccess)
            return Result.Fail(pluginsResult.Error!);

        var plugin = pluginsResult.Value!.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin is null)
        {
            return Result.Fail(new Error
            {
                Code = "PLUGIN_NOT_FOUND",
                UserMessage = $"未找到插件 {pluginId}",
                TechnicalMessage = $"PluginId {pluginId} not in installed list",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        // 查找 .uproject 文件
        var uprojectPath = FindUprojectFile(projectPath);
        if (uprojectPath is null)
        {
            return Result.Fail(new Error
            {
                Code = "UPROJECT_NOT_FOUND",
                UserMessage = "未找到 .uproject 项目文件",
                TechnicalMessage = $"No .uproject file in {projectPath}",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        try
        {
            var json = await File.ReadAllTextAsync(uprojectPath, ct);
            var node = JsonNode.Parse(json) ?? new JsonObject();

            var plugins = node["Plugins"]?.AsArray() ?? [];
            if (node["Plugins"] is null)
                node["Plugins"] = plugins;

            // 检查是否已存在
            var existing = FindPluginEntry(plugins, plugin.Name);
            if (existing is not null)
            {
                existing["Enabled"] = true;
                Logger.Information("插件 {Name} 已在项目中，已启用", plugin.Name);
            }
            else
            {
                plugins.Add(new JsonObject
                {
                    ["Name"] = plugin.Name,
                    ["Enabled"] = true,
                });
                Logger.Information("插件 {Name} 已添加到项目", plugin.Name);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = node.ToJsonString(options);
            await File.WriteAllTextAsync(uprojectPath, updatedJson, ct);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "编辑 .uproject 文件失败: {Path}", uprojectPath);
            return Result.Fail(new Error
            {
                Code = "UPROJECT_EDIT_FAILED",
                UserMessage = "修改项目文件失败",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    public async Task<Result> RemoveFromProjectAsync(string pluginId, string projectPath, CancellationToken ct)
    {
        Logger.Information("从项目 {Project} 移除插件 {PluginId}", projectPath, pluginId);

        var pluginsResult = await _pluginReadService.GetInstalledPluginsAsync(ct);
        if (!pluginsResult.IsSuccess)
            return Result.Fail(pluginsResult.Error!);

        var plugin = pluginsResult.Value!.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin is null)
        {
            return Result.Fail(new Error
            {
                Code = "PLUGIN_NOT_FOUND",
                UserMessage = $"未找到插件 {pluginId}",
                TechnicalMessage = $"PluginId {pluginId} not in installed list",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        var uprojectPath = FindUprojectFile(projectPath);
        if (uprojectPath is null)
        {
            return Result.Fail(new Error
            {
                Code = "UPROJECT_NOT_FOUND",
                UserMessage = "未找到 .uproject 项目文件",
                TechnicalMessage = $"No .uproject file in {projectPath}",
                CanRetry = false,
                Severity = ErrorSeverity.Warning,
            });
        }

        try
        {
            var json = await File.ReadAllTextAsync(uprojectPath, ct);
            var node = JsonNode.Parse(json) ?? new JsonObject();

            var plugins = node["Plugins"]?.AsArray();
            if (plugins is null)
            {
                Logger.Debug("项目无 Plugins 节点，无需移除");
                return Result.Ok();
            }

            var existing = FindPluginEntry(plugins, plugin.Name);
            if (existing is not null)
            {
                plugins.Remove(existing);
                Logger.Information("插件 {Name} 已从项目中移除", plugin.Name);

                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = node.ToJsonString(options);
                await File.WriteAllTextAsync(uprojectPath, updatedJson, ct);
            }
            else
            {
                Logger.Debug("插件 {Name} 不在项目中", plugin.Name);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "编辑 .uproject 文件失败: {Path}", uprojectPath);
            return Result.Fail(new Error
            {
                Code = "UPROJECT_EDIT_FAILED",
                UserMessage = "修改项目文件失败",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    private static string? FindUprojectFile(string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return null;

        var files = Directory.GetFiles(projectPath, "*.uproject", SearchOption.TopDirectoryOnly);
        return files.Length > 0 ? files[0] : null;
    }

    private static JsonNode? FindPluginEntry(JsonArray plugins, string pluginName)
    {
        foreach (var entry in plugins)
        {
            if (entry is JsonObject obj
                && obj["Name"]?.GetValue<string>() is { } name
                && name.Equals(pluginName, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }
        return null;
    }
}

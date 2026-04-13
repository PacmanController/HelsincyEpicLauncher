// Copyright (c) Helsincy. All rights reserved.

using System.Text.Json;
using Launcher.Application.Modules.EngineVersions.Contracts;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Application.Modules.Plugins.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.Plugins;

/// <summary>
/// 插件查询服务。扫描已安装资产中的插件，并检查引擎版本兼容性。
/// </summary>
public sealed class PluginReadService : IPluginReadService
{
    private static readonly ILogger Logger = Log.ForContext<PluginReadService>();

    private readonly IInstallReadService _installReadService;
    private readonly IEngineVersionReadService _engineVersionReadService;

    public PluginReadService(
        IInstallReadService installReadService,
        IEngineVersionReadService engineVersionReadService)
    {
        _installReadService = installReadService;
        _engineVersionReadService = engineVersionReadService;
    }

    public async Task<Result<IReadOnlyList<PluginSummary>>> GetInstalledPluginsAsync(CancellationToken ct)
    {
        try
        {
            var installed = await _installReadService.GetInstalledAsync(ct);

            // 过滤非引擎安装（UE_ 前缀是引擎，其余视为插件/资产）
            var plugins = new List<PluginSummary>();
            foreach (var item in installed)
            {
                if (item.AssetId.StartsWith("UE_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var summary = await ScanPluginAsync(item, ct);
                if (summary is not null)
                    plugins.Add(summary);
            }

            Logger.Information("发现 {Count} 个已安装插件", plugins.Count);
            return Result.Ok<IReadOnlyList<PluginSummary>>(plugins);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "扫描已安装插件失败");
            return Result.Fail<IReadOnlyList<PluginSummary>>(new Error
            {
                Code = "PLUGIN_SCAN_FAILED",
                UserMessage = "扫描插件失败",
                TechnicalMessage = ex.Message,
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }
    }

    public async Task<Result<CompatibilityReport>> CheckCompatibilityAsync(
        string pluginId, string engineVersionId, CancellationToken ct)
    {
        // 获取插件列表
        var pluginsResult = await GetInstalledPluginsAsync(ct);
        if (!pluginsResult.IsSuccess)
            return Result.Fail<CompatibilityReport>(pluginsResult.Error!);

        var plugin = pluginsResult.Value!.FirstOrDefault(p => p.PluginId == pluginId);
        if (plugin is null)
        {
            return Result.Ok(new CompatibilityReport
            {
                IsCompatible = false,
                IncompatibilityReason = $"插件 {pluginId} 未安装",
            });
        }

        // 如果插件声明了支持的引擎版本，检查是否包含目标版本
        if (plugin.SupportedEngineVersions.Count > 0)
        {
            var isCompatible = plugin.SupportedEngineVersions
                .Any(v => v.Equals(engineVersionId, StringComparison.OrdinalIgnoreCase));

            return Result.Ok(new CompatibilityReport
            {
                IsCompatible = isCompatible,
                IncompatibilityReason = isCompatible
                    ? null
                    : $"插件 {plugin.Name} 不支持引擎版本 {engineVersionId}",
            });
        }

        // 无版本声明时默认兼容
        return Result.Ok(new CompatibilityReport { IsCompatible = true });
    }

    private static Task<PluginSummary?> ScanPluginAsync(InstallStatusSummary item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 在安装目录中查找 .uplugin 文件以提取插件元数据
        var installPath = item.InstallPath;
        if (!Directory.Exists(installPath))
        {
            return Task.FromResult<PluginSummary?>(new PluginSummary
            {
                PluginId = item.AssetId,
                Name = item.AssetName,
                Version = item.Version ?? string.Empty,
                InstallPath = installPath,
                IsEnabled = true,
            });
        }

        // 搜索 .uplugin 文件
        var upluginFiles = Directory.GetFiles(installPath, "*.uplugin", SearchOption.AllDirectories);
        if (upluginFiles.Length == 0)
        {
            return Task.FromResult<PluginSummary?>(new PluginSummary
            {
                PluginId = item.AssetId,
                Name = item.AssetName,
                Version = item.Version ?? string.Empty,
                InstallPath = installPath,
                IsEnabled = true,
            });
        }

        // 解析第一个 .uplugin 文件
        try
        {
            var json = File.ReadAllText(upluginFiles[0]);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("FriendlyName", out var fn) ? fn.GetString() ?? item.AssetName : item.AssetName;
            var version = root.TryGetProperty("VersionName", out var vn) ? vn.GetString() ?? string.Empty : string.Empty;
            var author = root.TryGetProperty("CreatedBy", out var cb) ? cb.GetString() ?? string.Empty : string.Empty;

            var engineVersions = new List<string>();
            if (root.TryGetProperty("EngineVersion", out var ev) && ev.GetString() is { } evStr)
                engineVersions.Add(evStr);

            return Task.FromResult<PluginSummary?>(new PluginSummary
            {
                PluginId = item.AssetId,
                Name = name,
                Version = version,
                Author = author,
                InstallPath = installPath,
                SupportedEngineVersions = engineVersions,
                IsEnabled = true,
            });
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "解析 .uplugin 文件失败: {Path}", upluginFiles[0]);
            return Task.FromResult<PluginSummary?>(new PluginSummary
            {
                PluginId = item.AssetId,
                Name = item.AssetName,
                Version = item.Version ?? string.Empty,
                InstallPath = installPath,
                IsEnabled = true,
            });
        }
    }
}

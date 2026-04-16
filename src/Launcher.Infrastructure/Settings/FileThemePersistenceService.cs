// Copyright (c) Helsincy. All rights reserved.

using System.Text.Json;
using Launcher.Application.Modules.Settings.Contracts;
using Launcher.Shared.Configuration;
using Serilog;

namespace Launcher.Infrastructure.Settings;

/// <summary>
/// 基于文件的主题持久化实现。读写 {DataPath}/theme.json。
/// </summary>
internal sealed class FileThemePersistenceService : IThemePersistenceService
{
    private static readonly ILogger Logger = Log.ForContext<FileThemePersistenceService>();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    public FileThemePersistenceService(IAppConfigProvider configProvider)
    {
        _settingsPath = Path.Combine(configProvider.DataPath, "theme.json");
    }

    public async Task<string?> LoadThemeAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return null;

            var json = await File.ReadAllTextAsync(_settingsPath, ct);
            var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
            return settings?.Theme;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "加载主题设置失败");
            return null;
        }
    }

    public async Task SaveThemeAsync(string themeName, CancellationToken ct)
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var settings = new ThemeSettings { Theme = themeName };
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json, ct);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "保存主题设置失败");
        }
    }

    private sealed class ThemeSettings
    {
        public string Theme { get; set; } = "System";
    }
}

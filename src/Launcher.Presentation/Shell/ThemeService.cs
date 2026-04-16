// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Settings.Contracts;
using Microsoft.UI.Xaml;
using Serilog;

namespace Launcher.Presentation.Shell;

/// <summary>
/// 主题切换服务。管理 Light / Dark / System 三种主题。
/// 持久化通过 IThemePersistenceService 委托到 Infrastructure 层。
/// </summary>
public sealed class ThemeService
{
    private static readonly ILogger Logger = Log.ForContext<ThemeService>();

    private readonly IThemePersistenceService _persistence;
    private FrameworkElement? _rootElement;

    /// <summary>当前主题</summary>
    public ElementTheme CurrentTheme { get; private set; } = ElementTheme.Default;

    public ThemeService(IThemePersistenceService persistence)
    {
        _persistence = persistence;
    }

    /// <summary>
    /// 设置根元素并加载已保存的主题。由 ShellPage 在加载时调用。
    /// </summary>
    public async void Initialize(FrameworkElement rootElement)
    {
        _rootElement = rootElement;
        await LoadThemeAsync();
        ApplyTheme();
        Logger.Information("主题服务已初始化 | 当前主题: {Theme}", CurrentTheme);
    }

    /// <summary>
    /// 切换主题并持久化
    /// </summary>
    public async void SetTheme(ElementTheme theme)
    {
        CurrentTheme = theme;
        ApplyTheme();
        await SaveThemeAsync();
        Logger.Information("主题已切换为 {Theme}", theme);
    }

    private void ApplyTheme()
    {
        if (_rootElement is not null)
        {
            _rootElement.RequestedTheme = CurrentTheme;
        }
    }

    private async Task LoadThemeAsync()
    {
        var themeName = await _persistence.LoadThemeAsync();
        if (themeName is not null)
        {
            CurrentTheme = themeName switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    private async Task SaveThemeAsync()
    {
        var themeValue = CurrentTheme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "System",
        };
        await _persistence.SaveThemeAsync(themeValue);
    }
}

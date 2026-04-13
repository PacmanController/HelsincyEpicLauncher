// Copyright (c) Helsincy. All rights reserved.

using Launcher.Presentation.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Launcher.Presentation.Modules.Settings;

/// <summary>
/// 设置页面。通过 DI 解析 SettingsViewModel 并连接主题实时切换。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private static readonly ILogger Logger = Log.ForContext<SettingsPage>();

    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = ViewModelLocator.Resolve<SettingsViewModel>();
        this.InitializeComponent();

        // 监听主题变更请求，实时切换
        ViewModel.ThemeChangeRequested += OnThemeChangeRequested;
        Logger.Debug("SettingsPage 已创建");
    }

    private void OnThemeChangeRequested(string theme)
    {
        var themeService = ViewModelLocator.Resolve<ThemeService>();
        var elementTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        themeService.SetTheme(elementTheme);
    }
}

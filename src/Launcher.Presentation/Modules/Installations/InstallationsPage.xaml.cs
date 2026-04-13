// Copyright (c) Helsincy. All rights reserved.

using Launcher.Presentation.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace Launcher.Presentation.Modules.Installations;

/// <summary>
/// 已安装资产管理页面。
/// </summary>
public sealed partial class InstallationsPage : Page
{
    private static readonly ILogger Logger = Log.ForContext<InstallationsPage>();

    public InstallationsViewModel ViewModel { get; }

    public InstallationsPage()
    {
        ViewModel = ViewModelLocator.Resolve<InstallationsViewModel>();
        this.InitializeComponent();
        Logger.Debug("InstallationsPage 已创建");
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string assetId })
            await ViewModel.VerifyCommand.ExecuteAsync(assetId);
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string assetId })
            await ViewModel.RepairCommand.ExecuteAsync(assetId);
    }

    private async void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string assetId })
            await ViewModel.UninstallCommand.ExecuteAsync(assetId);
    }

    /// <summary>x:Bind 辅助：bool 取反转 Visibility（用于空状态）</summary>
    public static Visibility NotToVisibility(bool value)
        => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>x:Bind 辅助：bool 取反</summary>
    public static bool Not(bool value) => !value;

    /// <summary>x:Bind 辅助：非空字符串转 Visibility</summary>
    public static Visibility StringToVisibility(string? text)
        => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}

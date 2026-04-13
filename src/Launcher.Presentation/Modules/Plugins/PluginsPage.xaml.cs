// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Launcher.Presentation.Modules.Plugins;

/// <summary>
/// 插件管理页面。展示已安装插件列表和兼容性信息。
/// </summary>
public sealed partial class PluginsPage : Page
{
    public PluginsViewModel ViewModel { get; }

    public PluginsPage()
    {
        ViewModel = ViewModelLocator.Resolve<PluginsViewModel>();
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LoadCommand.CanExecute(null))
            ViewModel.LoadCommand.Execute(null);
    }

#pragma warning disable CA1822 // XAML event handlers must be instance methods
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LoadCommand.CanExecute(null))
            ViewModel.LoadCommand.Execute(null);
    }
#pragma warning restore CA1822
}

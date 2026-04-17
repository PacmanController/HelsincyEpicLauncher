// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using WinRT;

namespace Launcher.App;

/// <summary>
/// 应用程序入口点。初始化 WinUI 3 并启动 App。
/// </summary>
public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    internal static string InitialLaunchArguments { get; private set; } = string.Empty;

    [STAThread]
    public static void Main(string[] args)
    {
        InitialLaunchArguments = SingleInstanceCoordinator.ResolveProcessLaunchArguments(args);

        if (!SingleInstanceCoordinator.EnsureSingleInstance(InitialLaunchArguments))
        {
            return;
        }

        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}

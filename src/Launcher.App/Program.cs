// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace Launcher.App;

/// <summary>
/// 应用程序入口点。初始化 WinUI 3 并启动 App。
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}

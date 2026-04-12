// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Xaml;
using Serilog;

namespace Launcher.App;

/// <summary>
/// 主窗口。Phase 0 阶段仅显示空窗口，Phase 1 中加载 Shell 内容。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        // 设置窗口标题
        Title = "HelsincyEpicLauncher";

        // 设置最小窗口尺寸
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetMinWindowSize(hwnd, minWidth: 1024, minHeight: 640);

        Log.Debug("MainWindow 创建完成");
    }

    /// <summary>
    /// 通过 Win32 API 设置最小窗口尺寸
    /// </summary>
    private static void SetMinWindowSize(IntPtr hwnd, int minWidth, int minHeight)
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            // 设置默认窗口大小
            appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }
    }
}

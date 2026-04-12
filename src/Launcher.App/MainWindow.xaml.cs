// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace Launcher.App;

/// <summary>
/// 主窗口。自定义标题栏 + Mica 背景 + 最小窗口尺寸限制。
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        // 设置窗口标题
        Title = "HelsincyEpicLauncher";

        // 启用自定义标题栏（系统自动保留最小化/最大化/关闭按钮）
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Log.Debug("自定义标题栏已启用");

        // 设置 Mica 背景材质
        SystemBackdrop = new MicaBackdrop();
        Log.Debug("Mica 背景材质已设置");

        // 配置窗口尺寸
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ConfigureWindowSize(hwnd);

        Log.Information("MainWindow 创建完成 | 自定义标题栏 + Mica 背景");
    }

    /// <summary>
    /// 配置窗口尺寸：默认 1280x800，最小 1024x640
    /// </summary>
    private static void ConfigureWindowSize(IntPtr hwnd)
    {
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        // 设置默认窗口大小
        appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

        // 通过 Win32 子类化强制最小窗口尺寸
        PInvoke.SetMinWindowSize(hwnd, 1024, 640);

        Log.Debug("窗口尺寸已配置 | 默认 1280x800，最小 1024x640");
    }
}

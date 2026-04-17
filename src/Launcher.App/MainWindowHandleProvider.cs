// Copyright (c) Helsincy. All rights reserved.

using Launcher.Presentation.Shell;
using Microsoft.UI.Xaml;

namespace Launcher.App;

/// <summary>
/// App 组合根提供的主窗口句柄适配器。
/// </summary>
internal sealed class MainWindowHandleProvider : IWindowHandleProvider
{
    private IntPtr _mainWindowHandle;

    public IntPtr GetMainWindowHandle()
    {
        if (_mainWindowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("主窗口句柄尚未初始化");
        }

        return _mainWindowHandle;
    }

    public void SetMainWindow(Window window)
    {
        _mainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
    }
}
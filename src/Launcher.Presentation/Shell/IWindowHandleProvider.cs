// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Presentation.Shell;

/// <summary>
/// 提供主窗口句柄，供需要 Win32 窗口上下文的 Presentation 交互能力使用。
/// </summary>
public interface IWindowHandleProvider
{
    /// <summary>
    /// 获取主窗口 HWND。
    /// </summary>
    IntPtr GetMainWindowHandle();
}
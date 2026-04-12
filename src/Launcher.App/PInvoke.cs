// Copyright (c) Helsincy. All rights reserved.

using System.Runtime.InteropServices;

namespace Launcher.App;

/// <summary>
/// Win32 API 互操作。用于单实例激活窗口。
/// </summary>
internal static partial class PInvoke
{
    private const int SW_RESTORE = 9;

    /// <summary>
    /// 显示窗口（如果被最小化则恢复）
    /// </summary>
    public static void ShowWindow(IntPtr hwnd)
    {
        ShowWindow(hwnd, SW_RESTORE);
    }

    /// <summary>
    /// 将窗口设置为前台
    /// </summary>
    public static void SetForegroundWindow(IntPtr hwnd)
    {
        SetForegroundWindowNative(hwnd);
    }

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindowNative(IntPtr hWnd);
}

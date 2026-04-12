// Copyright (c) Helsincy. All rights reserved.

using System.Runtime.InteropServices;

namespace Launcher.App;

/// <summary>
/// Win32 API 互操作。用于单实例激活窗口和窗口尺寸限制。
/// </summary>
internal static partial class PInvoke
{
    private const int SW_RESTORE = 9;
    private const uint WM_GETMINMAXINFO = 0x0024;

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

    /// <summary>
    /// 设置窗口最小尺寸（DIP 单位，自动处理 DPI 缩放）。
    /// 通过 Win32 子类化拦截 WM_GETMINMAXINFO 实现。
    /// </summary>
    public static void SetMinWindowSize(IntPtr hwnd, int minWidthDip, int minHeightDip)
    {
        uint dpi = GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;
        _minWidthPx = (int)(minWidthDip * scale);
        _minHeightPx = (int)(minHeightDip * scale);

        // 保持委托引用防止 GC 回收
        _minSizeSubclassDelegate = MinSizeSubclassProc;
        SetWindowSubclass(hwnd, _minSizeSubclassDelegate, 0, 0);
    }

    // === 最小窗口尺寸子类化 ===

    private static int _minWidthPx;
    private static int _minHeightPx;
    private static SubclassProc? _minSizeSubclassDelegate;

    private static IntPtr MinSizeSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        nuint uIdSubclass, nuint dwRefData)
    {
        if (uMsg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = _minWidthPx;
            mmi.ptMinTrackSize.Y = _minHeightPx;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // === P/Invoke 声明 ===

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindowNative(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    // 使用 DllImport 而非 LibraryImport，因为涉及委托回调参数
    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // === 结构体 ===

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}

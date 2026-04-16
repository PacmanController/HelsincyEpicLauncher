// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Shared;

/// <summary>
/// 应用级常量。集中管理跨层共享的不变值。
/// </summary>
public static class AppConstants
{
    /// <summary>应用名称</summary>
    public const string AppName = "HelsincyEpicLauncher";

    public static class Download
    {
        /// <summary>磁盘空间检查系数（需要文件大小的 120%）</summary>
        public const double DiskSpaceBufferFactor = 1.2;

        /// <summary>历史记录默认查询限制</summary>
        public const int HistoryQueryLimit = 50;
    }

    public static class Network
    {
        /// <summary>断路器采样窗口（秒）</summary>
        public const int CircuitBreakerSamplingSeconds = 30;

        /// <summary>断路器中断时长（秒）</summary>
        public const int CircuitBreakerBreakSeconds = 30;
    }

    public static class Ui
    {
        /// <summary>默认分页大小</summary>
        public const int DefaultPageSize = 20;

        /// <summary>搜索防抖延迟（毫秒）</summary>
        public const int SearchDebounceMs = 300;
    }
}

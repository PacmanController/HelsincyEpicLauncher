// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Downloads;
using Launcher.Shared;

namespace Launcher.Infrastructure.Downloads;

/// <summary>
/// 下载模块错误工厂。集中管理错误码和消息模板。
/// </summary>
internal static class DownloadErrors
{
    public static Error NotFound(DownloadTaskId taskId) => new()
    {
        Code = "DL_NOT_FOUND",
        UserMessage = "下载任务不存在",
        TechnicalMessage = $"TaskId={taskId} not found",
        Severity = ErrorSeverity.Warning
    };

    public static Error InvalidPath(string path) => new()
    {
        Code = "DL_INVALID_PATH",
        UserMessage = "下载目标路径无效",
        TechnicalMessage = $"Cannot determine drive root for: {path}",
        Severity = ErrorSeverity.Error
    };

    public static Error InsufficientDiskSpace(long required, long available) => new()
    {
        Code = "DL_DISK_SPACE",
        UserMessage = "磁盘空间不足",
        TechnicalMessage = $"需要 {required} 字节，可用 {available} 字节",
        Severity = ErrorSeverity.Error
    };

    public static Error Duplicate(string assetId) => new()
    {
        Code = "DL_DUPLICATE",
        UserMessage = "该资产已在下载队列中",
        TechnicalMessage = $"AssetId={assetId} already exists",
        Severity = ErrorSeverity.Warning
    };

    public static Error CannotPause(DownloadTaskId taskId, DownloadState state) => new()
    {
        Code = "DL_CANNOT_PAUSE",
        UserMessage = "当前状态无法暂停",
        TechnicalMessage = $"TaskId={taskId} state={state} cannot pause",
        Severity = ErrorSeverity.Warning
    };

    public static Error CannotResume(DownloadTaskId taskId, DownloadState state) => new()
    {
        Code = "DL_CANNOT_RESUME",
        UserMessage = "当前状态无法恢复",
        TechnicalMessage = $"TaskId={taskId} state={state} cannot resume",
        Severity = ErrorSeverity.Warning
    };

    public static Error CannotCancel(DownloadTaskId taskId, DownloadState state) => new()
    {
        Code = "DL_CANNOT_CANCEL",
        UserMessage = "当前状态无法取消",
        TechnicalMessage = $"TaskId={taskId} state={state} cannot cancel",
        Severity = ErrorSeverity.Warning
    };
}

// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Downloads;
using Launcher.Shared;

namespace Launcher.Application.Modules.Downloads.Contracts;

/// <summary>
/// 下载任务编排器接口。管理下载任务的完整生命周期：入队、暂停、恢复、取消、崩溃恢复。
/// </summary>
public interface IDownloadOrchestrator
{
    /// <summary>
    /// 创建并入队下载任务
    /// </summary>
    Task<Result<DownloadTaskId>> EnqueueAsync(StartDownloadRequest request, CancellationToken ct);

    /// <summary>
    /// 暂停任务
    /// </summary>
    Task<Result> PauseAsync(DownloadTaskId taskId, CancellationToken ct);

    /// <summary>
    /// 恢复任务
    /// </summary>
    Task<Result> ResumeAsync(DownloadTaskId taskId, CancellationToken ct);

    /// <summary>
    /// 取消任务
    /// </summary>
    Task<Result> CancelAsync(DownloadTaskId taskId, CancellationToken ct);

    /// <summary>
    /// 崩溃恢复：加载所有未完成的任务并重新调度
    /// </summary>
    Task RecoverAsync(CancellationToken ct);

    /// <summary>
    /// 获取所有活跃（非 Paused/Completed/Cancelled/Failed）任务的 ID
    /// </summary>
    Task<IReadOnlyList<DownloadTaskId>> GetActiveTaskIdsAsync(CancellationToken ct);

    /// <summary>
    /// 获取所有已暂停任务的 ID
    /// </summary>
    Task<IReadOnlyList<DownloadTaskId>> GetPausedTaskIdsAsync(CancellationToken ct);

    /// <summary>
    /// 调整任务优先级
    /// </summary>
    Task<Result> SetPriorityAsync(DownloadTaskId taskId, int priority, CancellationToken ct);
}

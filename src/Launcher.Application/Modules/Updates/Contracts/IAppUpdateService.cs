// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Updates.Contracts;

/// <summary>
/// 启动器自身版本更新服务契约。
/// 负责检查更新、下载更新包、应用更新（退出后替换）。
/// 依赖：Settings.Contracts（读取更新检查频率），Launcher.Shared（Result 模型）。
/// Shell 通过订阅 UpdateAvailable 事件感知新版本，不直接依赖 Background 层。
/// </summary>
public interface IAppUpdateService
{
    /// <summary>
    /// 发现新版本时触发。由 AppUpdateWorker 检测到新版本后调用 NotifyUpdateAvailable 触发。
    /// Shell 层只需订阅此事件，无需了解 Worker 实现。
    /// </summary>
    event Action<UpdateAvailableEvent>? UpdateAvailable;

    /// <summary>
    /// 检查是否有新版本。返回 null 表示当前已是最新版本。
    /// </summary>
    Task<Result<UpdateInfo?>> CheckForUpdateAsync(CancellationToken ct);

    /// <summary>
    /// 下载并准备更新包，通过 progress 报告 0~1 进度。
    /// </summary>
    Task<Result> DownloadUpdateAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct);

    /// <summary>
    /// 应用更新（需要重启应用）：启动替换进程后退出当前应用。
    /// </summary>
    Task<Result> ApplyUpdateAsync(CancellationToken ct);

    /// <summary>
    /// 跳过此版本，后续检查时不再提示该版本。
    /// </summary>
    Task SkipVersionAsync(string version, CancellationToken ct);
}

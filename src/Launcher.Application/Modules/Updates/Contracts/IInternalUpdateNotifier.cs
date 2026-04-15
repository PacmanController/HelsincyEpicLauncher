// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Updates.Contracts;

/// <summary>
/// 内部通知接口。供 AppUpdateWorker 在检测到新版本后触发 UpdateAvailable 事件。
/// 避免 Background 层直接引用 Infrastructure 具体实现（AI-03: 不跨模块加依赖）。
/// </summary>
public interface IInternalUpdateNotifier
{
    /// <summary>
    /// 通知有新版本可用，触发 IAppUpdateService.UpdateAvailable 事件链。
    /// </summary>
    void NotifyUpdateAvailable(UpdateAvailableEvent evt);
}

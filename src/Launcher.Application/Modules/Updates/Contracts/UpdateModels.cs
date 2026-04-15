// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Updates.Contracts;

/// <summary>
/// 可用更新信息。
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>版本号（如 "1.2.0"）</summary>
    public string Version { get; init; } = default!;

    /// <summary>更新包下载地址</summary>
    public string DownloadUrl { get; init; } = default!;

    /// <summary>更新包大小（字节）</summary>
    public long DownloadSize { get; init; }

    /// <summary>版本变更日志</summary>
    public string ReleaseNotes { get; init; } = default!;

    /// <summary>发布日期</summary>
    public DateTime ReleaseDate { get; init; }

    /// <summary>是否强制更新</summary>
    public bool IsMandatory { get; init; }
}

/// <summary>
/// 发现新版本事件。由 AppUpdateWorker 在检测到新版本时通过 IAppUpdateService 发布。
/// </summary>
public sealed record UpdateAvailableEvent(string Version, bool IsMandatory);

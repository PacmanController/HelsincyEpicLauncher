// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Downloads;

namespace Launcher.Application.Modules.Downloads.Contracts;

/// <summary>
/// 下载任务摘要 DTO，用于列表展示。
/// </summary>
public sealed record DownloadTaskSummary(
    string Id,
    string AssetId,
    string DisplayName,
    DownloadUiState UiState,
    double ProgressPercent,
    long TotalBytes,
    long DownloadedBytes,
    long SpeedBytesPerSecond,
    int Priority,
    string? LastError,
    DateTimeOffset CreatedAt);

/// <summary>
/// 下载进度快照 DTO，用于实时进度更新。
/// </summary>
public sealed record DownloadProgressSnapshot(
    string TaskId,
    DownloadUiState UiState,
    double ProgressPercent,
    long DownloadedBytes,
    long TotalBytes,
    long SpeedBytesPerSecond);

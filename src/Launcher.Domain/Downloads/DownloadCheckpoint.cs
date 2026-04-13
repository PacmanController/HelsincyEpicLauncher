// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Domain.Downloads;

/// <summary>
/// 下载任务断点数据
/// </summary>
public sealed class DownloadCheckpoint
{
    public DownloadTaskId TaskId { get; init; }
    public string ManifestJson { get; init; } = string.Empty;
    public IReadOnlyList<ChunkCheckpoint> Chunks { get; init; } = [];
    public DateTime SavedAt { get; init; }
}

/// <summary>
/// 单个分块断点数据
/// </summary>
public sealed class ChunkCheckpoint
{
    public int ChunkIndex { get; init; }
    public long RangeStart { get; init; }
    public long RangeEnd { get; init; }
    public long DownloadedBytes { get; init; }
    public bool IsCompleted { get; init; }
    public string? PartialFilePath { get; init; }
    public string? Hash { get; init; }
}

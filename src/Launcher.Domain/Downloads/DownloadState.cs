// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Domain.Downloads;

/// <summary>
/// 下载任务内部状态。仅模块内部使用，不对外暴露。
/// </summary>
public enum DownloadState
{
    /// <summary>已入队，等待调度</summary>
    Queued,

    /// <summary>准备中（分配资源）</summary>
    Preparing,

    /// <summary>获取文件清单</summary>
    FetchingManifest,

    /// <summary>预分配磁盘空间</summary>
    AllocatingDisk,

    /// <summary>正在下载分块</summary>
    DownloadingChunks,

    /// <summary>某个分块失败，重试中</summary>
    RetryingChunk,

    /// <summary>正在暂停（等待活跃 chunk 完成保存）</summary>
    PausingChunks,

    /// <summary>已暂停</summary>
    Paused,

    /// <summary>下载完成后校验</summary>
    VerifyingDownload,

    /// <summary>最终处理（合并、清理临时文件）</summary>
    Finalizing,

    /// <summary>完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled,
}

/// <summary>
/// 对外 UI 状态。对其他模块和 UI 暴露。
/// </summary>
public enum DownloadUiState
{
    /// <summary>排队中</summary>
    Queued,

    /// <summary>下载中（包含准备/获取清单/分配磁盘/下载分块/重试）</summary>
    Downloading,

    /// <summary>已暂停</summary>
    Paused,

    /// <summary>校验中</summary>
    Verifying,

    /// <summary>完成</summary>
    Completed,

    /// <summary>失败</summary>
    Failed,

    /// <summary>已取消</summary>
    Cancelled,
}

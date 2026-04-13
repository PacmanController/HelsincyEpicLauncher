// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Common;

namespace Launcher.Domain.Downloads;

/// <summary>
/// 下载任务状态机。定义 13 个内部状态及合法转换规则。
/// </summary>
public sealed class DownloadStateMachine : StateMachine<DownloadState>
{
    public DownloadStateMachine() : this(DownloadState.Queued) { }

    public DownloadStateMachine(DownloadState initialState) : base(initialState)
    {
        // Queued → Preparing | Cancelled
        DefineTransition(DownloadState.Queued, DownloadState.Preparing);
        DefineTransition(DownloadState.Queued, DownloadState.Cancelled);

        // Preparing → FetchingManifest | Failed | Cancelled
        DefineTransition(DownloadState.Preparing, DownloadState.FetchingManifest);
        DefineTransition(DownloadState.Preparing, DownloadState.Failed);
        DefineTransition(DownloadState.Preparing, DownloadState.Cancelled);

        // FetchingManifest → AllocatingDisk | Failed | Cancelled
        DefineTransition(DownloadState.FetchingManifest, DownloadState.AllocatingDisk);
        DefineTransition(DownloadState.FetchingManifest, DownloadState.Failed);
        DefineTransition(DownloadState.FetchingManifest, DownloadState.Cancelled);

        // AllocatingDisk → DownloadingChunks | Failed | Cancelled
        DefineTransition(DownloadState.AllocatingDisk, DownloadState.DownloadingChunks);
        DefineTransition(DownloadState.AllocatingDisk, DownloadState.Failed);
        DefineTransition(DownloadState.AllocatingDisk, DownloadState.Cancelled);

        // DownloadingChunks → RetryingChunk | PausingChunks | VerifyingDownload | Failed | Cancelled
        DefineTransition(DownloadState.DownloadingChunks, DownloadState.RetryingChunk);
        DefineTransition(DownloadState.DownloadingChunks, DownloadState.PausingChunks);
        DefineTransition(DownloadState.DownloadingChunks, DownloadState.VerifyingDownload);
        DefineTransition(DownloadState.DownloadingChunks, DownloadState.Failed);
        DefineTransition(DownloadState.DownloadingChunks, DownloadState.Cancelled);

        // RetryingChunk → DownloadingChunks（重试成功） | Failed | Cancelled
        DefineTransition(DownloadState.RetryingChunk, DownloadState.DownloadingChunks);
        DefineTransition(DownloadState.RetryingChunk, DownloadState.Failed);
        DefineTransition(DownloadState.RetryingChunk, DownloadState.Cancelled);

        // PausingChunks → Paused
        DefineTransition(DownloadState.PausingChunks, DownloadState.Paused);

        // Paused → Queued（恢复） | Cancelled
        DefineTransition(DownloadState.Paused, DownloadState.Queued);
        DefineTransition(DownloadState.Paused, DownloadState.Cancelled);

        // VerifyingDownload → Finalizing | Failed
        DefineTransition(DownloadState.VerifyingDownload, DownloadState.Finalizing);
        DefineTransition(DownloadState.VerifyingDownload, DownloadState.Failed);

        // Finalizing → Completed | Failed
        DefineTransition(DownloadState.Finalizing, DownloadState.Completed);
        DefineTransition(DownloadState.Finalizing, DownloadState.Failed);

        // Failed → Queued（用户重试）
        DefineTransition(DownloadState.Failed, DownloadState.Queued);

        // Completed 和 Cancelled 是终态，无出边
    }

    /// <summary>
    /// 将内部状态映射到 UI 状态
    /// </summary>
    public static DownloadUiState MapToUiState(DownloadState internalState) => internalState switch
    {
        DownloadState.Queued => DownloadUiState.Queued,
        DownloadState.Preparing => DownloadUiState.Downloading,
        DownloadState.FetchingManifest => DownloadUiState.Downloading,
        DownloadState.AllocatingDisk => DownloadUiState.Downloading,
        DownloadState.DownloadingChunks => DownloadUiState.Downloading,
        DownloadState.RetryingChunk => DownloadUiState.Downloading,
        DownloadState.PausingChunks => DownloadUiState.Paused,
        DownloadState.Paused => DownloadUiState.Paused,
        DownloadState.VerifyingDownload => DownloadUiState.Verifying,
        DownloadState.Finalizing => DownloadUiState.Downloading,
        DownloadState.Completed => DownloadUiState.Completed,
        DownloadState.Failed => DownloadUiState.Failed,
        DownloadState.Cancelled => DownloadUiState.Cancelled,
        _ => DownloadUiState.Failed,
    };
}

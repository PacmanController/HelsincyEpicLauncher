// Copyright (c) Helsincy. All rights reserved.

using Launcher.Domain.Downloads;

namespace Launcher.Tests.Unit;

public class DownloadStateMachineTests
{
    // ===== 合法转换测试 =====

    [Theory]
    [InlineData(DownloadState.Queued, DownloadState.Preparing)]
    [InlineData(DownloadState.Queued, DownloadState.Cancelled)]
    [InlineData(DownloadState.Preparing, DownloadState.FetchingManifest)]
    [InlineData(DownloadState.Preparing, DownloadState.Failed)]
    [InlineData(DownloadState.Preparing, DownloadState.Cancelled)]
    [InlineData(DownloadState.FetchingManifest, DownloadState.AllocatingDisk)]
    [InlineData(DownloadState.FetchingManifest, DownloadState.Failed)]
    [InlineData(DownloadState.FetchingManifest, DownloadState.Cancelled)]
    [InlineData(DownloadState.AllocatingDisk, DownloadState.DownloadingChunks)]
    [InlineData(DownloadState.AllocatingDisk, DownloadState.Failed)]
    [InlineData(DownloadState.AllocatingDisk, DownloadState.Cancelled)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.RetryingChunk)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.PausingChunks)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.VerifyingDownload)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.Failed)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.Cancelled)]
    [InlineData(DownloadState.RetryingChunk, DownloadState.DownloadingChunks)]
    [InlineData(DownloadState.RetryingChunk, DownloadState.Failed)]
    [InlineData(DownloadState.RetryingChunk, DownloadState.Cancelled)]
    [InlineData(DownloadState.PausingChunks, DownloadState.Paused)]
    [InlineData(DownloadState.Paused, DownloadState.Queued)]
    [InlineData(DownloadState.Paused, DownloadState.Cancelled)]
    [InlineData(DownloadState.VerifyingDownload, DownloadState.Finalizing)]
    [InlineData(DownloadState.VerifyingDownload, DownloadState.Failed)]
    [InlineData(DownloadState.Finalizing, DownloadState.Completed)]
    [InlineData(DownloadState.Finalizing, DownloadState.Failed)]
    [InlineData(DownloadState.Failed, DownloadState.Queued)]
    public void TransitionTo_ValidTransition_Succeeds(DownloadState from, DownloadState to)
    {
        var sm = new DownloadStateMachine(from);

        var result = sm.TransitionTo(to);

        result.IsSuccess.Should().BeTrue();
        sm.Current.Should().Be(to);
    }

    // ===== 终态无出边测试 =====

    [Theory]
    [InlineData(DownloadState.Completed, DownloadState.Queued)]
    [InlineData(DownloadState.Completed, DownloadState.Failed)]
    [InlineData(DownloadState.Cancelled, DownloadState.Queued)]
    [InlineData(DownloadState.Cancelled, DownloadState.Failed)]
    public void TransitionTo_FromTerminalState_Fails(DownloadState from, DownloadState to)
    {
        var sm = new DownloadStateMachine(from);

        var result = sm.TransitionTo(to);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SM_INVALID_TRANSITION");
        sm.Current.Should().Be(from);
    }

    // ===== 非法转换测试 =====

    [Theory]
    [InlineData(DownloadState.Queued, DownloadState.DownloadingChunks)]
    [InlineData(DownloadState.Queued, DownloadState.Completed)]
    [InlineData(DownloadState.Preparing, DownloadState.Paused)]
    [InlineData(DownloadState.DownloadingChunks, DownloadState.Queued)]
    [InlineData(DownloadState.PausingChunks, DownloadState.DownloadingChunks)]
    [InlineData(DownloadState.Paused, DownloadState.DownloadingChunks)]
    [InlineData(DownloadState.VerifyingDownload, DownloadState.Cancelled)]
    [InlineData(DownloadState.Finalizing, DownloadState.Cancelled)]
    [InlineData(DownloadState.Failed, DownloadState.Completed)]
    public void TransitionTo_InvalidTransition_Fails(DownloadState from, DownloadState to)
    {
        var sm = new DownloadStateMachine(from);

        var result = sm.TransitionTo(to);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("SM_INVALID_TRANSITION");
        sm.Current.Should().Be(from);
    }

    // ===== CanTransitionTo 测试 =====

    [Fact]
    public void CanTransitionTo_ValidTarget_ReturnsTrue()
    {
        var sm = new DownloadStateMachine(DownloadState.Queued);

        sm.CanTransitionTo(DownloadState.Preparing).Should().BeTrue();
        sm.CanTransitionTo(DownloadState.Cancelled).Should().BeTrue();
    }

    [Fact]
    public void CanTransitionTo_InvalidTarget_ReturnsFalse()
    {
        var sm = new DownloadStateMachine(DownloadState.Queued);

        sm.CanTransitionTo(DownloadState.Completed).Should().BeFalse();
        sm.CanTransitionTo(DownloadState.DownloadingChunks).Should().BeFalse();
    }

    // ===== 默认初始状态测试 =====

    [Fact]
    public void DefaultConstructor_StartsAtQueued()
    {
        var sm = new DownloadStateMachine();

        sm.Current.Should().Be(DownloadState.Queued);
    }

    // ===== UI 状态映射测试 =====

    [Theory]
    [InlineData(DownloadState.Queued, DownloadUiState.Queued)]
    [InlineData(DownloadState.Preparing, DownloadUiState.Downloading)]
    [InlineData(DownloadState.FetchingManifest, DownloadUiState.Downloading)]
    [InlineData(DownloadState.AllocatingDisk, DownloadUiState.Downloading)]
    [InlineData(DownloadState.DownloadingChunks, DownloadUiState.Downloading)]
    [InlineData(DownloadState.RetryingChunk, DownloadUiState.Downloading)]
    [InlineData(DownloadState.PausingChunks, DownloadUiState.Paused)]
    [InlineData(DownloadState.Paused, DownloadUiState.Paused)]
    [InlineData(DownloadState.VerifyingDownload, DownloadUiState.Verifying)]
    [InlineData(DownloadState.Finalizing, DownloadUiState.Downloading)]
    [InlineData(DownloadState.Completed, DownloadUiState.Completed)]
    [InlineData(DownloadState.Failed, DownloadUiState.Failed)]
    [InlineData(DownloadState.Cancelled, DownloadUiState.Cancelled)]
    public void MapToUiState_ReturnsCorrectMapping(DownloadState internalState, DownloadUiState expectedUiState)
    {
        DownloadStateMachine.MapToUiState(internalState).Should().Be(expectedUiState);
    }

    // ===== 完整流程测试 =====

    [Fact]
    public void HappyPath_QueuedToCompleted_AllTransitionsSucceed()
    {
        var sm = new DownloadStateMachine();

        sm.TransitionTo(DownloadState.Preparing).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.FetchingManifest).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.AllocatingDisk).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.DownloadingChunks).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.VerifyingDownload).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.Finalizing).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.Completed).IsSuccess.Should().BeTrue();

        sm.Current.Should().Be(DownloadState.Completed);
    }

    [Fact]
    public void PauseResumePath_PauseAndResume_Succeeds()
    {
        var sm = new DownloadStateMachine();
        sm.TransitionTo(DownloadState.Preparing);
        sm.TransitionTo(DownloadState.FetchingManifest);
        sm.TransitionTo(DownloadState.AllocatingDisk);
        sm.TransitionTo(DownloadState.DownloadingChunks);

        // 暂停
        sm.TransitionTo(DownloadState.PausingChunks).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.Paused).IsSuccess.Should().BeTrue();

        // 恢复（回到 Queued 重新调度）
        sm.TransitionTo(DownloadState.Queued).IsSuccess.Should().BeTrue();

        sm.Current.Should().Be(DownloadState.Queued);
    }

    [Fact]
    public void RetryPath_FailAndRetry_Succeeds()
    {
        var sm = new DownloadStateMachine();
        sm.TransitionTo(DownloadState.Preparing);
        sm.TransitionTo(DownloadState.FetchingManifest);
        sm.TransitionTo(DownloadState.AllocatingDisk);
        sm.TransitionTo(DownloadState.DownloadingChunks);

        // Chunk 重试
        sm.TransitionTo(DownloadState.RetryingChunk).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.DownloadingChunks).IsSuccess.Should().BeTrue();

        // 整体失败后重试
        sm.TransitionTo(DownloadState.Failed).IsSuccess.Should().BeTrue();
        sm.TransitionTo(DownloadState.Queued).IsSuccess.Should().BeTrue();

        sm.Current.Should().Be(DownloadState.Queued);
    }
}

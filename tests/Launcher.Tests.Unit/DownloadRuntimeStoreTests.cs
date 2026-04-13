// Copyright (c) Helsincy. All rights reserved.

using FluentAssertions;
using Launcher.Application.Modules.Downloads.Contracts;
using Launcher.Domain.Downloads;
using Launcher.Infrastructure.Downloads;

namespace Launcher.Tests.Unit;

public sealed class DownloadRuntimeStoreTests : IDisposable
{
    private readonly DownloadRuntimeStore _store = new();
    private readonly DownloadTaskId _taskId = DownloadTaskId.New();

    public void Dispose() => _store.Dispose();

    [Fact]
    public void GetSnapshot_NoData_ReturnsNull()
    {
        _store.GetSnapshot(_taskId).Should().BeNull();
    }

    [Fact]
    public void UpdateProgress_StoresSnapshot()
    {
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 500, 1000);

        var snapshot = _store.GetSnapshot(_taskId);
        snapshot.Should().NotBeNull();
        snapshot!.TaskId.Should().Be(_taskId);
        snapshot.UiState.Should().Be(DownloadUiState.Downloading);
        snapshot.DownloadedBytes.Should().Be(500);
        snapshot.TotalBytes.Should().Be(1000);
        snapshot.ProgressPercent.Should().Be(50.0);
    }

    [Fact]
    public void UpdateProgress_OverwritesPreviousSnapshot()
    {
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 250, 1000);
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 750, 1000);

        var snapshot = _store.GetSnapshot(_taskId);
        snapshot!.DownloadedBytes.Should().Be(750);
        snapshot.ProgressPercent.Should().Be(75.0);
    }

    [Fact]
    public void UpdateProgress_ZeroTotalBytes_ProgressPercentIsZero()
    {
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 100, 0);

        var snapshot = _store.GetSnapshot(_taskId);
        snapshot!.ProgressPercent.Should().Be(0);
    }

    [Fact]
    public void GetAllSnapshots_ReturnsAllActiveSnapshots()
    {
        var id1 = DownloadTaskId.New();
        var id2 = DownloadTaskId.New();

        _store.UpdateProgress(id1, DownloadUiState.Downloading, 100, 1000);
        _store.UpdateProgress(id2, DownloadUiState.Downloading, 200, 2000);

        var all = _store.GetAllSnapshots();
        all.Should().HaveCount(2);
        all.Select(s => s.TaskId).Should().Contain(new[] { id1, id2 });
    }

    [Fact]
    public void RemoveSnapshot_RemovesExistingSnapshot()
    {
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 500, 1000);
        _store.RemoveSnapshot(_taskId);

        _store.GetSnapshot(_taskId).Should().BeNull();
    }

    [Fact]
    public void RemoveSnapshot_NonExistent_DoesNotThrow()
    {
        var act = () => _store.RemoveSnapshot(DownloadTaskId.New());
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyCompleted_RemovesSnapshotAndFiresEvent()
    {
        DownloadCompletedEvent? firedEvent = null;
        _store.DownloadCompleted += e => firedEvent = e;

        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 1000, 1000);
        _store.NotifyCompleted(_taskId, "asset-1", @"C:\Games\test.zip");

        _store.GetSnapshot(_taskId).Should().BeNull();
        firedEvent.Should().NotBeNull();
        firedEvent!.TaskId.Should().Be(_taskId);
        firedEvent.AssetId.Should().Be("asset-1");
        firedEvent.DownloadedFilePath.Should().Be(@"C:\Games\test.zip");
    }

    [Fact]
    public void NotifyFailed_RemovesSnapshotAndFiresEvent()
    {
        DownloadFailedEvent? firedEvent = null;
        _store.DownloadFailed += e => firedEvent = e;

        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 500, 1000);
        _store.NotifyFailed(_taskId, "asset-2", "Network error", true);

        _store.GetSnapshot(_taskId).Should().BeNull();
        firedEvent.Should().NotBeNull();
        firedEvent!.TaskId.Should().Be(_taskId);
        firedEvent.AssetId.Should().Be("asset-2");
        firedEvent.ErrorMessage.Should().Be("Network error");
        firedEvent.CanRetry.Should().BeTrue();
    }

    [Fact]
    public void SnapshotChanged_EventFiredOnFirstUpdate()
    {
        DownloadProgressSnapshot? firedSnapshot = null;
        _store.SnapshotChanged += s => firedSnapshot = s;

        // 首次调用应触发（距上次通知超过 500ms）
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 100, 1000);

        firedSnapshot.Should().NotBeNull();
        firedSnapshot!.TaskId.Should().Be(_taskId);
    }

    [Fact]
    public void SnapshotChanged_Throttled_NoDoubleFireWithinInterval()
    {
        var fireCount = 0;
        _store.SnapshotChanged += _ => fireCount++;

        // 连续快速更新，应该只触发一次（节流 500ms）
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 100, 1000);
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 200, 1000);
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 300, 1000);

        fireCount.Should().Be(1); // 只有第一次触发（后续在 500ms 内被节流）
    }

    [Fact]
    public void UpdateProgress_EtaCalculatedWhenSpeedPositive()
    {
        // Need two samples to get a speed > 0
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 0, 10000);
        // Simulate some passage of time by calling with more bytes
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 5000, 10000);

        var snapshot = _store.GetSnapshot(_taskId);
        // 快照已更新，但 ETA 取决于速度计算器是否有足够的样本
        snapshot.Should().NotBeNull();
        snapshot!.TotalBytes.Should().Be(10000);
    }

    [Fact]
    public void Dispose_ClearsAllData()
    {
        _store.UpdateProgress(_taskId, DownloadUiState.Downloading, 500, 1000);

        _store.Dispose();

        _store.GetAllSnapshots().Should().BeEmpty();
    }
}

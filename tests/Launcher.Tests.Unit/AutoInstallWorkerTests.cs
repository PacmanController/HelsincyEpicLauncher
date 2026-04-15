// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Downloads.Contracts;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Application.Modules.Settings.Contracts;
using Launcher.Background.Installations;
using Launcher.Domain.Downloads;
using Launcher.Shared;
using Launcher.Shared.Configuration;

namespace Launcher.Tests.Unit;

/// <summary>
/// AutoInstallWorker 单元测试。验证事件驱动的自动安装逻辑。
/// </summary>
public sealed class AutoInstallWorkerTests : IDisposable
{
    private readonly IDownloadRuntimeStore _runtimeStore = Substitute.For<IDownloadRuntimeStore>();
    private readonly IDownloadReadService _downloadReadService = Substitute.For<IDownloadReadService>();
    private readonly ISettingsReadService _settingsReadService = Substitute.For<ISettingsReadService>();
    private readonly IInstallCommandService _installCommandService = Substitute.For<IInstallCommandService>();
    private readonly IAppConfigProvider _configProvider = Substitute.For<IAppConfigProvider>();
    private readonly AutoInstallWorker _sut;

    public AutoInstallWorkerTests()
    {
        _configProvider.InstallPath.Returns(@"C:\TestInstalls");

        _sut = new AutoInstallWorker(
            _runtimeStore,
            _downloadReadService,
            _settingsReadService,
            _installCommandService,
            _configProvider);

        _sut.Start();
    }

    public void Dispose() => _sut.Dispose();

    [Fact]
    public async Task AutoInstallEnabled_DownloadCompleted_CallsInstallAsync()
    {
        // Arrange
        _settingsReadService.GetDownloadConfig().Returns(new DownloadConfig { AutoInstall = true });
        _downloadReadService.GetStatusAsync("asset-1", Arg.Any<CancellationToken>())
            .Returns(new DownloadStatusSummary
            {
                TaskId = DownloadTaskId.New(),
                AssetId = "asset-1",
                AssetName = "Test Asset",
                UiState = DownloadUiState.Completed,
                Progress = 1.0,
            });
        _installCommandService.InstallAsync(Arg.Any<InstallRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Act: Raise the event
        _runtimeStore.DownloadCompleted += Raise.Event<Action<DownloadCompletedEvent>>(
            new DownloadCompletedEvent(
                DownloadTaskId.New(),
                "asset-1",
                @"C:\Downloads\asset-1.zip"));

        // Allow async event handler to complete
        await Task.Delay(200);

        // Assert
        await _installCommandService.Received(1).InstallAsync(
            Arg.Is<InstallRequest>(r => r.AssetId == "asset-1" && r.AssetName == "Test Asset"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoInstallDisabled_DownloadCompleted_DoesNotCallInstallAsync()
    {
        // Arrange
        _settingsReadService.GetDownloadConfig().Returns(new DownloadConfig { AutoInstall = false });

        // Act
        _runtimeStore.DownloadCompleted += Raise.Event<Action<DownloadCompletedEvent>>(
            new DownloadCompletedEvent(
                DownloadTaskId.New(),
                "asset-1",
                @"C:\Downloads\asset-1.zip"));

        await Task.Delay(200);

        // Assert
        await _installCommandService.DidNotReceive().InstallAsync(
            Arg.Any<InstallRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoInstallEnabled_InstallFails_DoesNotThrow()
    {
        // Arrange
        _settingsReadService.GetDownloadConfig().Returns(new DownloadConfig { AutoInstall = true });
        _downloadReadService.GetStatusAsync("asset-1", Arg.Any<CancellationToken>())
            .Returns(new DownloadStatusSummary
            {
                TaskId = DownloadTaskId.New(),
                AssetId = "asset-1",
                AssetName = "Test Asset",
                UiState = DownloadUiState.Completed,
                Progress = 1.0,
            });
        _installCommandService.InstallAsync(Arg.Any<InstallRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail(new Error
            {
                Code = "INSTALL_FAILED",
                UserMessage = "Install failed",
                TechnicalMessage = "Disk full",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            }));

        // Act & Assert: should not throw
        _runtimeStore.DownloadCompleted += Raise.Event<Action<DownloadCompletedEvent>>(
            new DownloadCompletedEvent(
                DownloadTaskId.New(),
                "asset-1",
                @"C:\Downloads\asset-1.zip"));

        await Task.Delay(200);

        // Verify it was called but didn't crash
        await _installCommandService.Received(1).InstallAsync(
            Arg.Any<InstallRequest>(), Arg.Any<CancellationToken>());
    }
}

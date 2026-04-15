// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Domain.Installations;
using Launcher.Infrastructure.Installations;
using Launcher.Shared;

namespace Launcher.Tests.Unit;

/// <summary>
/// InstallCommandService.RepairAsync 单元测试。
/// Mock 所有依赖，验证各种修复场景。
/// </summary>
public sealed class RepairAsyncTests
{
    private readonly IInstallationRepository _repository = Substitute.For<IInstallationRepository>();
    private readonly InstallWorker _worker;
    private readonly IIntegrityVerifier _verifier = Substitute.For<IIntegrityVerifier>();
    private readonly IRepairDownloadUrlProvider _urlProvider = Substitute.For<IRepairDownloadUrlProvider>();
    private readonly RepairFileDownloader _repairDownloader;
    private readonly InstallCommandService _sut;

    public RepairAsyncTests()
    {
        _worker = new InstallWorker(_repository);

        // RepairFileDownloader 需要 IHttpClientFactory, IHashingService
        var hashingService = Substitute.For<IHashingService>();
        var httpFactory = Substitute.For<IHttpClientFactory>();
        _repairDownloader = new RepairFileDownloader(httpFactory, hashingService);

        _sut = new InstallCommandService(_repository, _worker, _verifier, _urlProvider, _repairDownloader);
    }

    private static Installation CreateTestInstallation(InstallState state = InstallState.Installed)
    {
        return new Installation(
            id: "inst-1",
            assetId: "asset-1",
            assetName: "Test Asset",
            version: "1.0.0",
            installPath: @"C:\TestInstall\asset-1",
            sizeBytes: 1024,
            assetType: "FabAsset",
            state: state,
            installedAt: DateTimeOffset.UtcNow.AddDays(-1),
            updatedAt: DateTimeOffset.UtcNow.AddDays(-1));
    }

    private static InstallManifest CreateTestManifest()
    {
        return new InstallManifest
        {
            AssetId = "asset-1",
            Version = "1.0.0",
            Files =
            [
                new ManifestFileEntry { RelativePath = "data/file1.bin", Size = 512, Hash = "aabbccdd" },
                new ManifestFileEntry { RelativePath = "data/file2.bin", Size = 512, Hash = "eeff0011" },
            ],
            TotalSize = 1024,
        };
    }

    [Fact]
    public async Task RepairAsync_NotFound_ReturnsError()
    {
        // Arrange
        _repository.GetByAssetIdAsync("missing", Arg.Any<CancellationToken>())
            .Returns((Installation?)null);

        // Act
        var result = await _sut.RepairAsync("missing", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INSTALL_NOT_FOUND");
    }

    [Fact]
    public async Task RepairAsync_VerificationPassed_ReturnsOk()
    {
        // Arrange
        var installation = CreateTestInstallation();
        var manifest = CreateTestManifest();

        _repository.GetByAssetIdAsync("asset-1", Arg.Any<CancellationToken>()).Returns(installation);
        _repository.GetManifestAsync("asset-1", Arg.Any<CancellationToken>()).Returns(manifest);
        _verifier.VerifyInstallationAsync(
            Arg.Any<string>(), Arg.Any<InstallManifest>(), Arg.Any<IProgress<VerificationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new VerificationReport { IsValid = true, TotalFilesChecked = 2 }));

        // Act
        var result = await _sut.RepairAsync("asset-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        installation.State.Should().Be(InstallState.Installed);
    }

    [Fact]
    public async Task RepairAsync_NoManifest_ReturnsFailed()
    {
        // Arrange
        var installation = CreateTestInstallation();

        _repository.GetByAssetIdAsync("asset-1", Arg.Any<CancellationToken>()).Returns(installation);
        _repository.GetManifestAsync("asset-1", Arg.Any<CancellationToken>()).Returns((InstallManifest?)null);

        // Act
        var result = await _sut.RepairAsync("asset-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("REPAIR_NO_MANIFEST");
        installation.State.Should().Be(InstallState.Failed);
    }

    [Fact]
    public async Task RepairAsync_UrlProviderFails_ReturnsNeedsRepair()
    {
        // Arrange
        var installation = CreateTestInstallation();
        var manifest = CreateTestManifest();
        var damagedReport = new VerificationReport
        {
            IsValid = false,
            MissingFiles = ["data/file1.bin"],
            CorruptedFiles = [],
            TotalFilesChecked = 2,
        };

        _repository.GetByAssetIdAsync("asset-1", Arg.Any<CancellationToken>()).Returns(installation);
        _repository.GetManifestAsync("asset-1", Arg.Any<CancellationToken>()).Returns(manifest);
        _verifier.VerifyInstallationAsync(
            Arg.Any<string>(), Arg.Any<InstallManifest>(), Arg.Any<IProgress<VerificationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(damagedReport));
        _urlProvider.GetDownloadInfoAsync("asset-1", Arg.Any<CancellationToken>())
            .Returns(Result.Fail<RepairDownloadInfo>(new Error
            {
                Code = "REPAIR_URL_FAILED",
                UserMessage = "Cannot get URL",
                TechnicalMessage = "API failed",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            }));

        // Act
        var result = await _sut.RepairAsync("asset-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("REPAIR_URL_FAILED");
        installation.State.Should().Be(InstallState.NeedsRepair);
    }

    [Fact]
    public async Task RepairAsync_VerificationFails_ReturnsError()
    {
        // Arrange
        var installation = CreateTestInstallation();
        var manifest = CreateTestManifest();

        _repository.GetByAssetIdAsync("asset-1", Arg.Any<CancellationToken>()).Returns(installation);
        _repository.GetManifestAsync("asset-1", Arg.Any<CancellationToken>()).Returns(manifest);
        _verifier.VerifyInstallationAsync(
            Arg.Any<string>(), Arg.Any<InstallManifest>(), Arg.Any<IProgress<VerificationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Fail<VerificationReport>(new Error
            {
                Code = "VERIFY_IO_ERROR",
                UserMessage = "IO error during verify",
                TechnicalMessage = "Disk read failure",
                CanRetry = false,
                Severity = ErrorSeverity.Error,
            }));

        // Act
        var result = await _sut.RepairAsync("asset-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("VERIFY_IO_ERROR");
        installation.State.Should().Be(InstallState.Failed);
    }

    [Fact]
    public async Task RepairAsync_InvalidStateTransition_ReturnsError()
    {
        // Arrange: Installation in NotInstalled state cannot transition to Repairing
        var installation = CreateTestInstallation(InstallState.NotInstalled);

        _repository.GetByAssetIdAsync("asset-1", Arg.Any<CancellationToken>()).Returns(installation);

        // Act
        var result = await _sut.RepairAsync("asset-1", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }
}

// Copyright (c) Helsincy. All rights reserved.

using System.Security.Cryptography;
using FluentAssertions;
using Launcher.Application.Modules.Installations.Contracts;
using Launcher.Domain.Installations;
using Launcher.Infrastructure.Installations;

namespace Launcher.Tests.Unit;

public sealed class IntegrityVerifierTests : IDisposable
{
    private readonly HashingService _hashingService = new();
    private readonly IntegrityVerifier _sut;
    private readonly string _installDir = Path.Combine(Path.GetTempPath(), $"verify_test_{Guid.NewGuid():N}");

    public IntegrityVerifierTests()
    {
        _sut = new IntegrityVerifier(_hashingService);
        Directory.CreateDirectory(_installDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_installDir))
            Directory.Delete(_installDir, true);
    }

    private (string Path, string Hash) CreateFileWithHash(string relativePath, string content)
    {
        var fullPath = Path.Combine(_installDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        var hash = Convert.ToHexStringLower(hashBytes);
        return (fullPath, hash);
    }

    // ===== VerifyFileAsync =====

    [Fact]
    public async Task VerifyFileAsync_MatchingHash_ReturnsTrue()
    {
        var (path, hash) = CreateFileWithHash("test.txt", "hello world");

        var result = await _sut.VerifyFileAsync(path, hash, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyFileAsync_MismatchedHash_ReturnsFalse()
    {
        var (path, _) = CreateFileWithHash("test.txt", "hello world");

        var result = await _sut.VerifyFileAsync(path, "0000000000000000000000000000000000000000000000000000000000000000", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyFileAsync_missing_file_fails()
    {
        var result = await _sut.VerifyFileAsync(
            Path.Combine(_installDir, "nope.txt"),
            "abc",
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // ===== VerifyInstallationAsync =====

    [Fact]
    public async Task VerifyInstallationAsync_AllValid_ReturnsIsValid()
    {
        var (_, hash1) = CreateFileWithHash("data/a.bin", "aaa");
        var (_, hash2) = CreateFileWithHash("data/b.bin", "bbb");

        var manifest = new InstallManifest
        {
            AssetId = "test-asset",
            Version = "1.0",
            Files =
            [
                new ManifestFileEntry { RelativePath = "data/a.bin", Hash = hash1, Size = 3 },
                new ManifestFileEntry { RelativePath = "data/b.bin", Hash = hash2, Size = 3 },
            ],
            TotalSize = 6,
        };

        var result = await _sut.VerifyInstallationAsync(_installDir, manifest, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsValid.Should().BeTrue();
        result.Value.MissingFiles.Should().BeEmpty();
        result.Value.CorruptedFiles.Should().BeEmpty();
        result.Value.TotalFilesChecked.Should().Be(2);
    }

    [Fact]
    public async Task VerifyInstallationAsync_MissingFile_ReportsMissing()
    {
        var (_, hash1) = CreateFileWithHash("exists.txt", "data");

        var manifest = new InstallManifest
        {
            AssetId = "test-asset",
            Version = "1.0",
            Files =
            [
                new ManifestFileEntry { RelativePath = "exists.txt", Hash = hash1, Size = 4 },
                new ManifestFileEntry { RelativePath = "gone.txt", Hash = "abc", Size = 10 },
            ],
            TotalSize = 14,
        };

        var result = await _sut.VerifyInstallationAsync(_installDir, manifest, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsValid.Should().BeFalse();
        result.Value.MissingFiles.Should().Contain("gone.txt");
    }

    [Fact]
    public async Task VerifyInstallationAsync_CorruptedFile_ReportsCorrupted()
    {
        CreateFileWithHash("ok.txt", "good");
        CreateFileWithHash("bad.txt", "tampered content");

        var manifest = new InstallManifest
        {
            AssetId = "test-asset",
            Version = "1.0",
            Files =
            [
                new ManifestFileEntry { RelativePath = "ok.txt", Hash = GetSha256("good"), Size = 4 },
                new ManifestFileEntry { RelativePath = "bad.txt", Hash = "aaaa", Size = 16 },
            ],
            TotalSize = 20,
        };

        var result = await _sut.VerifyInstallationAsync(_installDir, manifest, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsValid.Should().BeFalse();
        result.Value.CorruptedFiles.Should().Contain("bad.txt");
    }

    [Fact]
    public async Task VerifyInstallationAsync_EmptyManifest_ReportsValid()
    {
        var manifest = new InstallManifest
        {
            AssetId = "empty",
            Version = "1.0",
            Files = [],
            TotalSize = 0,
        };

        var result = await _sut.VerifyInstallationAsync(_installDir, manifest, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyInstallationAsync_ReportsProgress()
    {
        var (_, hash) = CreateFileWithHash("single.txt", "content");
        var manifest = new InstallManifest
        {
            AssetId = "prog",
            Version = "1.0",
            Files = [new ManifestFileEntry { RelativePath = "single.txt", Hash = hash, Size = 7 }],
            TotalSize = 7,
        };
        var progressReports = new List<VerificationProgress>();
        var progress = new Progress<VerificationProgress>(p => progressReports.Add(p));

        var result = await _sut.VerifyInstallationAsync(_installDir, manifest, progress, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await Task.Delay(100); // Progress 回调异步
        progressReports.Should().NotBeEmpty();
    }

    private static string GetSha256(string content)
    {
        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hashBytes);
    }
}

// Copyright (c) Helsincy. All rights reserved.

using FluentAssertions;
using Launcher.Infrastructure.Installations;

namespace Launcher.Tests.Unit;

public sealed class HashingServiceTests : IDisposable
{
    private readonly HashingService _sut = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"hashing_test_{Guid.NewGuid():N}");

    public HashingServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string CreateTempFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ===== ComputeHashAsync =====

    [Fact]
    public async Task ComputeHashAsync_ValidFile_ReturnsHexHash()
    {
        var path = CreateTempFile("hello.txt", "hello");
        var result = await _sut.ComputeHashAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        result.Value!.Length.Should().Be(64); // SHA-256 = 64 hex chars
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_ReturnsSameHash()
    {
        var path1 = CreateTempFile("a.txt", "test content");
        var path2 = CreateTempFile("b.txt", "test content");

        var r1 = await _sut.ComputeHashAsync(path1, CancellationToken.None);
        var r2 = await _sut.ComputeHashAsync(path2, CancellationToken.None);

        r1.Value.Should().Be(r2.Value);
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_ReturnsDifferentHash()
    {
        var path1 = CreateTempFile("x.txt", "aaa");
        var path2 = CreateTempFile("y.txt", "bbb");

        var r1 = await _sut.ComputeHashAsync(path1, CancellationToken.None);
        var r2 = await _sut.ComputeHashAsync(path2, CancellationToken.None);

        r1.Value.Should().NotBe(r2.Value);
    }

    [Fact]
    public async Task ComputeHashAsync_FileNotFound_Fails()
    {
        var result = await _sut.ComputeHashAsync(Path.Combine(_tempDir, "nonexistent.txt"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("HASH_FILE_NOT_FOUND");
    }

    [Fact]
    public async Task ComputeHashAsync_Cancelled_Fails()
    {
        var path = CreateTempFile("cancel.txt", "data");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _sut.ComputeHashAsync(path, cts.Token);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("HASH_CANCELLED");
    }

    // ===== ComputeHashesAsync =====

    [Fact]
    public async Task ComputeHashesAsync_MultipleFiles_ReturnsAllHashes()
    {
        var paths = new List<string>
        {
            CreateTempFile("f1.txt", "file1"),
            CreateTempFile("f2.txt", "file2"),
            CreateTempFile("f3.txt", "file3"),
        };

        var result = await _sut.ComputeHashesAsync(paths, 2, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(3);
    }

    [Fact]
    public async Task ComputeHashesAsync_ReportsProgress()
    {
        var paths = new List<string>
        {
            CreateTempFile("p1.txt", "data1"),
            CreateTempFile("p2.txt", "data2"),
        };
        var progressValues = new List<int>();
        var progress = new Progress<int>(v => progressValues.Add(v));

        var result = await _sut.ComputeHashesAsync(paths, 2, progress, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Progress 回调可能异步，等待到达
        await Task.Delay(100);
        progressValues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ComputeHashesAsync_EmptyList_ReturnsEmptyDict()
    {
        var result = await _sut.ComputeHashesAsync([], 4, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(0);
    }
}

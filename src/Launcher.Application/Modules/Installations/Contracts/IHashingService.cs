// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Installations.Contracts;

/// <summary>
/// 哈希计算服务接口。
/// </summary>
public interface IHashingService
{
    /// <summary>计算文件的 SHA-256 哈希</summary>
    Task<Result<string>> ComputeHashAsync(string filePath, CancellationToken ct);

    /// <summary>并行计算多个文件的哈希</summary>
    Task<Result<IReadOnlyDictionary<string, string>>> ComputeHashesAsync(
        IReadOnlyList<string> filePaths,
        int maxParallelism,
        IProgress<int>? progress,
        CancellationToken ct);
}

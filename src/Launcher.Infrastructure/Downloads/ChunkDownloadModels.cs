// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Infrastructure.Downloads;

/// <summary>
/// 分块下载请求
/// </summary>
public sealed class ChunkDownloadRequest
{
    /// <summary>CDN 下载 URL</summary>
    public required string Url { get; init; }

    /// <summary>临时文件路径</summary>
    public required string DestinationPath { get; init; }

    /// <summary>HTTP Range 起始字节</summary>
    public long RangeStart { get; init; }

    /// <summary>HTTP Range 结束字节</summary>
    public long RangeEnd { get; init; }

    /// <summary>预期哈希（可选校验）</summary>
    public string? ExpectedHash { get; init; }
}

/// <summary>
/// 分块下载结果
/// </summary>
public sealed class ChunkDownloadResult
{
    /// <summary>实际下载的字节数</summary>
    public long BytesDownloaded { get; init; }

    /// <summary>文件 SHA-256 哈希</summary>
    public string ActualHash { get; init; } = string.Empty;

    /// <summary>哈希是否匹配</summary>
    public bool HashMatch { get; init; }
}

// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.Installations.Contracts;

/// <summary>
/// 修复下载 URL 提供者。通过依赖倒置解决 Installations 模块获取新鲜 CDN URL 的需求。
/// 由 Infrastructure 层实现（内部调用 FabApiClient）。
/// </summary>
public interface IRepairDownloadUrlProvider
{
    /// <summary>获取指定资产的最新下载信息（用于修复）</summary>
    Task<Result<RepairDownloadInfo>> GetDownloadInfoAsync(string assetId, CancellationToken ct);
}

/// <summary>
/// 修复所需的下载信息
/// </summary>
public sealed class RepairDownloadInfo
{
    /// <summary>CDN 下载 URL（最新有效链接）</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>文件名</summary>
    public required string FileName { get; init; }

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; init; }
}

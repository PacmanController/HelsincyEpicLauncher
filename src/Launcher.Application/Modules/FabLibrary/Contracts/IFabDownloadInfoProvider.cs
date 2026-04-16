// Copyright (c) Helsincy. All rights reserved.

using Launcher.Shared;

namespace Launcher.Application.Modules.FabLibrary.Contracts;

/// <summary>
/// Fab 资产下载信息提供者。供跨模块调用方（如 Installations）获取最新 CDN 下载链接。
/// </summary>
public interface IFabDownloadInfoProvider
{
    /// <summary>获取指定资产的下载信息（URL、文件名、大小）</summary>
    Task<Result<FabDownloadInfo>> GetDownloadInfoAsync(string assetId, CancellationToken ct);
}

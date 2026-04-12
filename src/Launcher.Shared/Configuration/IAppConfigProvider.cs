// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Shared.Configuration;

/// <summary>
/// 应用级配置提供器接口。提供对强类型配置的只读访问。
/// </summary>
public interface IAppConfigProvider
{
    /// <summary>
    /// 应用版本号
    /// </summary>
    string AppVersion { get; }

    /// <summary>
    /// 本地数据目录（%LOCALAPPDATA%/HelsincyEpicLauncher/Data）
    /// </summary>
    string DataPath { get; }

    /// <summary>
    /// 日志目录（%LOCALAPPDATA%/HelsincyEpicLauncher/Logs）
    /// </summary>
    string LogPath { get; }

    /// <summary>
    /// 缓存目录（%LOCALAPPDATA%/HelsincyEpicLauncher/Cache）
    /// </summary>
    string CachePath { get; }

    /// <summary>
    /// 下载目录（用户可配置）
    /// </summary>
    string DownloadPath { get; }

    /// <summary>
    /// 安装目录（用户可配置）
    /// </summary>
    string InstallPath { get; }

    /// <summary>
    /// 最大并行下载任务数
    /// </summary>
    int MaxConcurrentDownloads { get; }

    /// <summary>
    /// 每个下载任务的最大并行分块数
    /// </summary>
    int MaxChunksPerDownload { get; }
}

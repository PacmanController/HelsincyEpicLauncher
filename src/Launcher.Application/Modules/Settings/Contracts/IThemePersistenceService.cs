// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Settings.Contracts;

/// <summary>
/// 主题持久化服务接口。负责从文件系统读写主题偏好。
/// Presentation 层通过此接口委托 I/O，避免直接操作文件系统。
/// </summary>
public interface IThemePersistenceService
{
    /// <summary>加载已保存的主题名称（"Light"/"Dark"/"System"）。无保存记录返回 null。</summary>
    Task<string?> LoadThemeAsync(CancellationToken ct = default);

    /// <summary>保存主题名称</summary>
    Task SaveThemeAsync(string themeName, CancellationToken ct = default);
}

// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Application.Modules.Diagnostics.Contracts;

/// <summary>
/// 诊断只读查询服务。提供系统信息和日志查询功能。
/// </summary>
public interface IDiagnosticsReadService
{
    /// <summary>获取系统诊断摘要</summary>
    Task<SystemDiagnosticsSummary> GetSystemSummaryAsync(CancellationToken ct = default);

    /// <summary>获取最近日志条目</summary>
    Task<IReadOnlyList<LogEntry>> GetRecentLogsAsync(int count, LogEntryLevel? minLevel = null, CancellationToken ct = default);

    /// <summary>搜索日志（关键字 + 可选时间范围）</summary>
    Task<IReadOnlyList<LogEntry>> SearchLogsAsync(string keyword, LogEntryLevel? minLevel = null, CancellationToken ct = default);
}

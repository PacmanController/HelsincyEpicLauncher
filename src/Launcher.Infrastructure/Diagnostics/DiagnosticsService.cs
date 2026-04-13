// Copyright (c) Helsincy. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Launcher.Application.Modules.Diagnostics.Contracts;
using Launcher.Shared.Configuration;
using Serilog;

namespace Launcher.Infrastructure.Diagnostics;

/// <summary>
/// 诊断服务实现。收集系统信息、磁盘空间、内存使用等诊断数据，读取 Serilog 日志文件。
/// </summary>
internal sealed class DiagnosticsService : IDiagnosticsReadService
{
    private static readonly ILogger Logger = Log.ForContext<DiagnosticsService>();
    private static readonly DateTime AppStartTime = DateTime.UtcNow;

    private readonly IAppConfigProvider _configProvider;

    public DiagnosticsService(IAppConfigProvider configProvider)
    {
        _configProvider = configProvider;
        Logger.Debug("诊断服务已初始化");
    }

    public Task<SystemDiagnosticsSummary> GetSystemSummaryAsync(CancellationToken ct = default)
    {
        var process = Process.GetCurrentProcess();

        // 磁盘空间（数据目录所在磁盘）
        long availableDiskMb = 0;
        long totalDiskMb = 0;
        try
        {
            var dataPath = _configProvider.DataPath;
            var driveRoot = Path.GetPathRoot(dataPath);
            if (!string.IsNullOrEmpty(driveRoot))
            {
                var driveInfo = new DriveInfo(driveRoot);
                availableDiskMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
                totalDiskMb = driveInfo.TotalSize / (1024 * 1024);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "获取磁盘空间信息失败");
        }

        // 内存信息
        long totalMemoryMb = 0;
        long usedMemoryMb = 0;
        try
        {
            totalMemoryMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
            usedMemoryMb = totalMemoryMb - (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes - process.WorkingSet64) / (1024 * 1024);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "获取内存信息失败");
        }

        // 数据库文件大小
        long dbSizeMb = 0;
        try
        {
            var dbPath = Path.Combine(_configProvider.DataPath, "launcher.db");
            if (File.Exists(dbPath))
            {
                dbSizeMb = new FileInfo(dbPath).Length / (1024 * 1024);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "获取数据库文件大小失败");
        }

        var summary = new SystemDiagnosticsSummary
        {
            OsVersion = $"{Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})",
            DotNetVersion = Environment.Version.ToString(),
            AppVersion = _configProvider.AppVersion,
            AppStartedAt = AppStartTime,
            AvailableDiskSpaceMb = availableDiskMb,
            TotalDiskSpaceMb = totalDiskMb,
            TotalMemoryMb = totalMemoryMb,
            UsedMemoryMb = usedMemoryMb,
            ProcessMemoryMb = process.WorkingSet64 / (1024 * 1024),
            DatabaseSizeMb = dbSizeMb,
        };

        Logger.Debug("系统诊断摘要已生成 | OS={Os} | 可用磁盘={DiskMb}MB | 进程内存={MemMb}MB",
            summary.OsVersion, summary.AvailableDiskSpaceMb, summary.ProcessMemoryMb);

        return Task.FromResult(summary);
    }

    public async Task<IReadOnlyList<LogEntry>> GetRecentLogsAsync(int count, LogEntryLevel? minLevel = null, CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();

        try
        {
            var logFiles = GetLogFiles();
            foreach (var file in logFiles)
            {
                ct.ThrowIfCancellationRequested();
                var fileEntries = await ReadLogFileAsync(file, ct);
                entries.AddRange(fileEntries);
            }

            // 按时间降序排列并过滤级别
            var result = entries
                .Where(e => !minLevel.HasValue || e.Level >= minLevel.Value)
                .OrderByDescending(e => e.Timestamp)
                .Take(count)
                .ToList();

            Logger.Debug("获取最近日志 | 总计={Total} | 筛选后={Filtered} | 最小级别={Level}",
                entries.Count, result.Count, minLevel);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "读取日志文件失败");
            return entries;
        }
    }

    public async Task<IReadOnlyList<LogEntry>> SearchLogsAsync(string keyword, LogEntryLevel? minLevel = null, CancellationToken ct = default)
    {
        var entries = new List<LogEntry>();

        try
        {
            var logFiles = GetLogFiles();
            foreach (var file in logFiles)
            {
                ct.ThrowIfCancellationRequested();
                var fileEntries = await ReadLogFileAsync(file, ct);
                entries.AddRange(fileEntries);
            }

            var result = entries
                .Where(e => !minLevel.HasValue || e.Level >= minLevel.Value)
                .Where(e =>
                    e.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (e.Source.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    (e.CorrelationId is not null && e.CorrelationId.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                    (e.Exception is not null && e.Exception.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.Timestamp)
                .Take(500)
                .ToList();

            Logger.Debug("搜索日志 | 关键字={Keyword} | 匹配={Count}", keyword, result.Count);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Warning(ex, "搜索日志失败 | 关键字={Keyword}", keyword);
            return entries;
        }
    }

    /// <summary>
    /// 获取当天的日志文件列表
    /// </summary>
    private List<string> GetLogFiles()
    {
        var logDir = _configProvider.LogPath;
        if (!Directory.Exists(logDir))
            return [];

        // 读取最近的 app-*.log 和 error-*.log 文件
        return Directory.GetFiles(logDir, "*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(3) // 最近 3 个日志文件
            .ToList();
    }

    /// <summary>
    /// 读取 Serilog CompactJSON 格式的日志文件
    /// </summary>
    private static async Task<List<LogEntry>> ReadLogFileAsync(string filePath, CancellationToken ct)
    {
        var entries = new List<LogEntry>();

        // 使用共享读模式打开（Serilog 写锁兼容）
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var entry = ParseCompactJsonLine(line);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// 解析单行 Serilog Compact JSON 日志
    /// 格式：{"@t":"...","@mt":"...","@l":"Information","SourceContext":"...","CorrelationId":"...",...}
    /// </summary>
    private static LogEntry? ParseCompactJsonLine(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 时间戳 @t
            var timestamp = DateTime.UtcNow;
            if (root.TryGetProperty("@t", out var tProp))
            {
                DateTime.TryParse(tProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
            }

            // 级别 @l（默认 Information）
            var level = LogEntryLevel.Information;
            if (root.TryGetProperty("@l", out var lProp))
            {
                level = ParseLevel(lProp.GetString());
            }

            // 消息 @mt（模板）或 @m（渲染后）
            var message = string.Empty;
            if (root.TryGetProperty("@m", out var mProp))
            {
                message = mProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("@mt", out var mtProp))
            {
                message = mtProp.GetString() ?? string.Empty;
            }

            // 来源 SourceContext
            var source = string.Empty;
            if (root.TryGetProperty("SourceContext", out var scProp))
            {
                source = scProp.GetString() ?? string.Empty;
                // 简化：只取类名部分
                var lastDot = source.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < source.Length - 1)
                    source = source[(lastDot + 1)..];
            }

            // 异常 @x
            string? exception = null;
            if (root.TryGetProperty("@x", out var xProp))
            {
                exception = xProp.GetString();
            }

            // CorrelationId
            string? correlationId = null;
            if (root.TryGetProperty("CorrelationId", out var cidProp))
            {
                correlationId = cidProp.GetString();
            }

            return new LogEntry
            {
                Timestamp = timestamp,
                Level = level,
                Source = source,
                Message = message,
                Exception = exception,
                CorrelationId = correlationId,
            };
        }
        catch
        {
            // 解析失败的行静默跳过
            return null;
        }
    }

    private static LogEntryLevel ParseLevel(string? level) => level switch
    {
        "Verbose" or "Debug" => LogEntryLevel.Debug,
        "Information" => LogEntryLevel.Information,
        "Warning" => LogEntryLevel.Warning,
        "Error" => LogEntryLevel.Error,
        "Fatal" => LogEntryLevel.Fatal,
        _ => LogEntryLevel.Information,
    };
}

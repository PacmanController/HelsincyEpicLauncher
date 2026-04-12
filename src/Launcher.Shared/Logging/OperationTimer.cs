// Copyright (c) Helsincy. All rights reserved.

using System.Diagnostics;
using Serilog;

namespace Launcher.Shared.Logging;

/// <summary>
/// 操作计时器。使用 using 模式自动记录操作耗时。
/// </summary>
public sealed class OperationTimer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch;

    public OperationTimer(ILogger logger, string operationName)
    {
        _logger = logger;
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();

        _logger.Information("{Operation} 开始", _operationName);
    }

    /// <summary>
    /// 操作耗时（毫秒）
    /// </summary>
    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public void Dispose()
    {
        _stopwatch.Stop();
        _logger.Information("{Operation} 完成 | 耗时 {Duration}ms",
            _operationName, _stopwatch.ElapsedMilliseconds);
    }
}

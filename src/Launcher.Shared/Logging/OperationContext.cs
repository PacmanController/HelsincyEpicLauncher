// Copyright (c) Helsincy. All rights reserved.

using Serilog.Context;

namespace Launcher.Shared.Logging;

/// <summary>
/// 操作上下文。每次用户操作创建一个，沿调用链传递。
/// 所有日志通过同一 CorrelationId 关联，实现全链路追踪。
/// </summary>
public sealed class OperationContext
{
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public string Module { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public DateTime StartedAt { get; } = DateTime.UtcNow;

    /// <summary>
    /// 将上下文字段推入 Serilog LogContext，返回的 IDisposable 在 using 块结束时自动清理。
    /// </summary>
    public IDisposable PushToLogContext()
    {
        var d1 = LogContext.PushProperty("CorrelationId", CorrelationId);
        var d2 = LogContext.PushProperty("Module", Module);
        var d3 = LogContext.PushProperty("Operation", Operation);
        return new CompositeDisposable(d1, d2, d3);
    }

    /// <summary>
    /// 组合多个 IDisposable，按反序释放。
    /// </summary>
    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _disposables;

        public CompositeDisposable(params IDisposable[] disposables)
        {
            _disposables = disposables;
        }

        public void Dispose()
        {
            // 按反序释放，与 LogContext 的栈语义一致
            for (int i = _disposables.Length - 1; i >= 0; i--)
            {
                _disposables[i].Dispose();
            }
        }
    }
}

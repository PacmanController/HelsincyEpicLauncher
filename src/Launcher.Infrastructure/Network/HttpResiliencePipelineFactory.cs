// Copyright (c) Helsincy. All rights reserved.

using Polly;
using Polly.Retry;
using Serilog;

namespace Launcher.Infrastructure.Network;

/// <summary>
/// HTTP 韧性管道工厂。集中管理 Polly 重试 + 超时策略，避免各 API 客户端重复配置。
/// </summary>
internal static class HttpResiliencePipelineFactory
{
    /// <summary>
    /// 创建默认 HTTP 韧性管道：3 次指数退避重试 + 30 秒超时。
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> CreateDefault(ILogger? logger = null)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                OnRetry = args =>
                {
                    logger?.Warning("HTTP 重试 #{Attempt}, 延迟 {Delay}ms",
                        args.AttemptNumber, args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();
    }
}

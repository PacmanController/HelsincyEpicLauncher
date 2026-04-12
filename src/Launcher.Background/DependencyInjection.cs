// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Background;

/// <summary>
/// Background 层 DI 注册扩展
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddBackground(this IServiceCollection services)
    {
        // 后台服务注册将在后续任务中添加

        return services;
    }
}

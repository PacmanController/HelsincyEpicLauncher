// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Application;

/// <summary>
/// Application 层 DI 注册扩展
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // 用例服务注册将在后续任务中添加

        return services;
    }
}

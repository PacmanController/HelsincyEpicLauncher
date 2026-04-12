// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Domain;

/// <summary>
/// Domain 层 DI 注册扩展
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDomain(this IServiceCollection services)
    {
        // 领域服务注册将在后续任务中添加

        return services;
    }
}

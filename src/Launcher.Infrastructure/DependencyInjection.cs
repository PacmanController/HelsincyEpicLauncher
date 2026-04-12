// Copyright (c) Helsincy. All rights reserved.

using Launcher.Infrastructure.Configuration;
using Launcher.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Infrastructure;

/// <summary>
/// Infrastructure 层 DI 注册扩展
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // 配置
        services.AddSingleton<IAppConfigProvider, AppConfigProvider>();

        return services;
    }
}

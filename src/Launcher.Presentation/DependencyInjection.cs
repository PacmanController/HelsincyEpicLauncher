// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Presentation;

/// <summary>
/// Presentation 层 DI 注册扩展
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // ViewModel 和 View 注册将在后续任务中添加

        return services;
    }
}

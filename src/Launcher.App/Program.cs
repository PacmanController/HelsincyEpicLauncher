// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Launcher.Application;
using Launcher.Background;
using Launcher.Domain;
using Launcher.Infrastructure;
using Launcher.Presentation;

namespace Launcher.App;

/// <summary>
/// 应用程序入口点。构建配置 + DI 容器，启动 WinUI 3 应用。
/// </summary>
public static class Program
{
    /// <summary>
    /// 全局服务提供器（供 WinUI 3 App 类访问）
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        // 构建配置
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        // 构建 DI 容器
        var services = new ServiceCollection();

        // 注册配置实例
        services.AddSingleton(configuration);

        // 按层注册服务
        services.AddDomain();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPresentation();
        services.AddBackground();

        Services = services.BuildServiceProvider();

        // Task 0.7 中启动 WinUI 3 应用
    }
}

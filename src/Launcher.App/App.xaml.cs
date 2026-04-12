// Copyright (c) Helsincy. All rights reserved.

using Microsoft.UI.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Launcher.Application;
using Launcher.Background;
using Launcher.Domain;
using Launcher.Infrastructure;
using Launcher.Presentation;
using Launcher.Shared.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Globalization;
using System.IO.Pipes;

namespace Launcher.App;

/// <summary>
/// WinUI 3 应用入口。管理单实例、DI 容器、日志初始化和启动流程。
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    /// <summary>
    /// 单实例互斥体名称
    /// </summary>
    private const string MutexName = "HelsincyEpicLauncher_SingleInstance";

    /// <summary>
    /// 命名管道名称（用于实例间通信）
    /// </summary>
    private const string PipeName = "HelsincyEpicLauncher_Pipe";

    private static Mutex? _mutex;
    private MainWindow? _mainWindow;

    /// <summary>
    /// 全局服务提供器
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// WinUI 3 应用启动入口
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // === Phase 0：单实例检查 + 最小可显示 ===
        if (!EnsureSingleInstance())
        {
            Log.Information("检测到已有实例运行，已通知前台，退出当前实例");
            Environment.Exit(0);
            return;
        }

        // 启动管道监听（接收第二实例的激活通知）
        StartPipeListener();

        // === Phase 1：核心初始化（DI + 配置 + 日志 + 数据库） ===
        InitializeCoreServices();

        // 创建主窗口
        _mainWindow = new MainWindow();
        _mainWindow.Activate();

        Log.Information("主窗口已显示");
    }

    /// <summary>
    /// 单实例检查。如果已有实例运行，通过命名管道通知并返回 false。
    /// </summary>
    private static bool EnsureSingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // 通知已有实例激活窗口
            NotifyExistingInstance();
            return false;
        }
        return true;
    }

    /// <summary>
    /// 通过命名管道通知已有实例激活主窗口
    /// </summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);
            using var writer = new StreamWriter(client);
            writer.WriteLine("ACTIVATE");
            writer.Flush();
        }
        catch (Exception ex)
        {
            // 管道通信失败不阻塞退出
            Log.Warning(ex, "命名管道通知已有实例失败");
        }
    }

    /// <summary>
    /// 启动命名管道监听，接收第二实例的激活请求
    /// </summary>
    private void StartPipeListener()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server);
                    string? message = await reader.ReadLineAsync();

                    if (message == "ACTIVATE")
                    {
                        Log.Information("收到第二实例的激活请求，激活主窗口");
                        ActivateMainWindow();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "命名管道监听异常");
                }
            }
        });
    }

    /// <summary>
    /// 在 UI 线程上激活主窗口（从后台线程调度）
    /// </summary>
    private void ActivateMainWindow()
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (_mainWindow is not null)
            {
                // 如果窗口被最小化，恢复到正常状态
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow);
                PInvoke.ShowWindow(hwnd);
                PInvoke.SetForegroundWindow(hwnd);
            }
        });
    }

    /// <summary>
    /// 初始化核心服务：配置 + DI + Serilog + 数据库迁移
    /// </summary>
    private static void InitializeCoreServices()
    {
        // 构建配置
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        // 构建 DI 容器
        var services = new ServiceCollection();
        services.AddSingleton(configuration);

        // 按层注册服务
        services.AddDomain();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPresentation();
        services.AddBackground();

        Services = services.BuildServiceProvider();

        // 初始化 Serilog
        var configProvider = Services.GetRequiredService<IAppConfigProvider>();
        InitializeSerilog(configProvider);

        Log.Information("应用启动 | 版本 {AppVersion}", configProvider.AppVersion);
        Log.Information("DI 容器初始化完成");

        // 执行数据库迁移
        try
        {
            var dbInitializer = Services.GetRequiredService<Launcher.Application.Persistence.IDatabaseInitializer>();
            dbInitializer.InitializeAsync().GetAwaiter().GetResult();
            Log.Information("数据库迁移执行完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据库迁移失败");
        }
    }

    /// <summary>
    /// 初始化 Serilog 日志系统。
    /// 三路文件 Sink：主日志、错误日志、下载日志。
    /// </summary>
    private static void InitializeSerilog(IAppConfigProvider configProvider)
    {
        string logDir = configProvider.LogPath;
        string appVersion = configProvider.AppVersion;

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("AppVersion", appVersion)
            .Enrich.FromLogContext()
            // 主日志文件：Information 及以上，按天轮转，保留 30 天
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 100_000_000,
                rollOnFileSizeLimit: true,
                restrictedToMinimumLevel: LogEventLevel.Information)
            // 错误日志：仅 Error + Fatal，保留 90 天
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(logDir, "error-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 90,
                restrictedToMinimumLevel: LogEventLevel.Error)
            // 下载模块专用日志：Debug 级别，保留 14 天
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e =>
                    e.Properties.TryGetValue("SourceContext", out var sv)
                    && sv.ToString().Contains("Downloads"))
                .WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(logDir, "download-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 200_000_000));

#if DEBUG
        // 开发时控制台输出
        loggerConfig = loggerConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{Module}/{Operation}] {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture);
#endif

        Log.Logger = loggerConfig.CreateLogger();
    }
}

// Copyright (c) Helsincy. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Launcher.Presentation;

/// <summary>
/// Frame.Navigate 创建的页面无法使用构造器注入。
/// 此静态定位器由 App 启动时注入 IServiceProvider，页面通过它解析 ViewModel。
/// </summary>
public static class ViewModelLocator
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// 由 App 启动时调用，注入全局 DI 容器。
    /// </summary>
    public static void Configure(IServiceProvider serviceProvider) =>
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    /// <summary>
    /// 从 DI 容器解析指定类型。
    /// </summary>
    public static T Resolve<T>() where T : notnull =>
        (_serviceProvider ?? throw new InvalidOperationException("ViewModelLocator 未初始化，请先调用 Configure"))
            .GetRequiredService<T>();
}

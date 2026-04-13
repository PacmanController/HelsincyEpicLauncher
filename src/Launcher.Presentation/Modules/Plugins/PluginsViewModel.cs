// Copyright (c) Helsincy. All rights reserved.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Application.Modules.Plugins.Contracts;
using Serilog;

namespace Launcher.Presentation.Modules.Plugins;

/// <summary>
/// 插件管理页面 ViewModel。展示已安装插件列表，支持兼容性检查和项目集成。
/// </summary>
public partial class PluginsViewModel : ObservableObject
{
    private static readonly ILogger Logger = Log.ForContext<PluginsViewModel>();

    private readonly IPluginReadService _readService;
    private readonly IPluginCommandService _commandService;

    /// <summary>已安装插件列表</summary>
    public ObservableCollection<PluginItemViewModel> Plugins { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isNotLoading = true;
    [ObservableProperty] private bool _hasPlugins;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasError;

    partial void OnIsLoadingChanged(bool value)
    {
        IsNotLoading = !value;
        IsEmpty = !HasPlugins && !value;
    }

    partial void OnErrorMessageChanged(string? value) => HasError = !string.IsNullOrEmpty(value);

    public PluginsViewModel(
        IPluginReadService readService,
        IPluginCommandService commandService)
    {
        _readService = readService;
        _commandService = commandService;

        Logger.Debug("PluginsViewModel 已创建");
    }

    /// <summary>页面加载时刷新列表</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _readService.GetInstalledPluginsAsync(CancellationToken.None);
            if (result.IsSuccess)
            {
                Plugins.Clear();
                foreach (var p in result.Value!)
                    Plugins.Add(new PluginItemViewModel(p));

                HasPlugins = Plugins.Count > 0;
                Logger.Information("插件列表已加载：{Count} 个插件", Plugins.Count);
            }
            else
            {
                ErrorMessage = result.Error?.UserMessage ?? "加载插件列表失败";
                Logger.Warning("加载插件失败: {Error}", result.Error?.TechnicalMessage);
            }
        }
        finally
        {
            IsLoading = false;
            IsEmpty = !HasPlugins && !IsLoading;
        }
    }

    /// <summary>检查兼容性</summary>
    [RelayCommand]
    private async Task CheckCompatibilityAsync(PluginItemViewModel item)
    {
        if (string.IsNullOrEmpty(item.EngineVersionId)) return;

        var result = await _readService.CheckCompatibilityAsync(
            item.PluginId, item.EngineVersionId, CancellationToken.None);

        if (result.IsSuccess)
        {
            item.CompatibilityText = result.Value!.IsCompatible ? "兼容" : result.Value.IncompatibilityReason ?? "不兼容";
            item.IsCompatible = result.Value.IsCompatible;
        }
    }
}

/// <summary>插件项 ViewModel</summary>
public partial class PluginItemViewModel : ObservableObject
{
    public string PluginId { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; }
    public string InstallPath { get; }
    public string SupportedVersionsText { get; }

    /// <summary>用于兼容性检查的目标引擎版本 ID（可选）</summary>
    public string? EngineVersionId { get; set; }

    [ObservableProperty] private string? _compatibilityText;
    [ObservableProperty] private bool? _isCompatible;

    public PluginItemViewModel(PluginSummary summary)
    {
        PluginId = summary.PluginId;
        Name = summary.Name;
        Version = summary.Version;
        Author = summary.Author;
        InstallPath = summary.InstallPath;
        SupportedVersionsText = summary.SupportedEngineVersions.Count > 0
            ? string.Join(", ", summary.SupportedEngineVersions)
            : "全版本";
    }
}

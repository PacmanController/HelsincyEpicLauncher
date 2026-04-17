// Copyright (c) Helsincy. All rights reserved.

using Launcher.Presentation.Shell;
using Serilog;
using Windows.Storage.Pickers;

namespace Launcher.Presentation.Modules.Settings;

/// <summary>
/// Settings 页面使用的文件夹选择器接口。
/// 该能力属于 Presentation 层纯交互，不进入 Application/Infrastructure。
/// </summary>
internal interface IFolderPickerService
{
    /// <summary>
    /// 打开系统文件夹选择器，返回用户选择的路径；取消时返回 null。
    /// </summary>
    Task<string?> PickFolderAsync(CancellationToken ct);
}

/// <summary>
/// WinUI 原生文件夹选择器封装。
/// </summary>
internal sealed class FolderPickerService : IFolderPickerService
{
    private static readonly ILogger Logger = Log.ForContext<FolderPickerService>();
    private readonly IWindowHandleProvider _windowHandleProvider;

    public FolderPickerService(IWindowHandleProvider windowHandleProvider)
    {
        _windowHandleProvider = windowHandleProvider;
    }

    public async Task<string?> PickFolderAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, _windowHandleProvider.GetMainWindowHandle());

        var folder = await picker.PickSingleFolderAsync();
        ct.ThrowIfCancellationRequested();

        if (folder is null)
        {
            Logger.Debug("用户取消文件夹选择");
            return null;
        }

        Logger.Debug("文件夹选择完成 | Path={Path}", folder.Path);
        return folder.Path;
    }
}
// Copyright (c) Helsincy. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Serilog;

namespace Launcher.Presentation.Shell;

/// <summary>
/// 对话框服务实现。使用 WinUI 3 ContentDialog 显示确认、信息和错误对话框。
/// 需在 UI 线程上调用。
/// </summary>
public sealed class DialogService : IDialogService
{
    private static readonly ILogger Logger = Log.ForContext<DialogService>();
    private XamlRoot? _xamlRoot;

    /// <summary>
    /// 设置 XamlRoot。由 ShellPage 在加载时调用，ContentDialog 显示需要此引用。
    /// </summary>
    public void SetXamlRoot(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
        Logger.Debug("DialogService XamlRoot 已设置");
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "确认", string cancelText = "取消")
    {
        Logger.Information("显示确认对话框 | {Title}", title);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot,
        };

        var result = await dialog.ShowAsync();
        bool confirmed = result == ContentDialogResult.Primary;

        Logger.Information("确认对话框结果 | {Title} → {Confirmed}", title, confirmed);
        return confirmed;
    }

    public async Task ShowInfoAsync(string title, string message)
    {
        Logger.Information("显示信息对话框 | {Title}", title);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _xamlRoot,
        };

        await dialog.ShowAsync();
    }

    public async Task ShowErrorAsync(string title, string message, bool canRetry = false)
    {
        Logger.Warning("显示错误对话框 | {Title} CanRetry={CanRetry}", title, canRetry);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = canRetry ? "取消" : "确定",
            DefaultButton = canRetry ? ContentDialogButton.Primary : ContentDialogButton.Close,
            XamlRoot = _xamlRoot,
        };

        if (canRetry)
        {
            dialog.PrimaryButtonText = "重试";
        }

        await dialog.ShowAsync();
    }

    public async Task<string?> ShowTextInputAsync(string title, string message, string placeholder = "", string confirmText = "确认", string cancelText = "取消")
    {
        Logger.Information("显示文本输入对话框 | {Title}", title);

        var inputBox = new TextBox
        {
            PlaceholderText = placeholder,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            MinWidth = 360,
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                },
                inputBox,
            },
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? inputBox.Text?.Trim() : null;
    }

    public async Task<Result<string>> ShowEpicExchangeCodeLoginAsync(AuthExchangeCodeLoginContext loginContext, CancellationToken ct = default)
    {
        Logger.Information("显示 Epic 嵌入式登录对话框");

        if (_xamlRoot is null)
        {
            return Result.Fail<string>(new Error
            {
                Code = "AUTH_WEBVIEW_XAML_ROOT_MISSING",
                UserMessage = "登录窗口尚未准备完成，请稍后重试",
                TechnicalMessage = "DialogService XamlRoot is not set before showing Epic embedded login dialog.",
                CanRetry = true,
                Severity = ErrorSeverity.Error,
            });
        }

        var statusText = new TextBlock
        {
            Text = "正在加载安全登录窗口...",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var progressRing = new ProgressRing
        {
            IsActive = true,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = 620,
        };

        var content = new Grid
        {
            Width = 920,
            Height = 720,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                progressRing,
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "请在此窗口中完成 Epic 登录",
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        },
                        statusText,
                    },
                },
            },
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(webView, 1);
        content.Children.Add(header);
        content.Children.Add(webView);

        var dialog = new ContentDialog
        {
            Title = "登录 Epic Games",
            Content = content,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _xamlRoot,
        };

        string? exchangeCode = null;
        Error? dialogError = null;
        bool dialogClosed = false;

        void CloseDialog()
        {
            if (dialogClosed)
            {
                return;
            }

            dialogClosed = true;
            try
            {
                dialog.Hide();
            }
            catch (InvalidOperationException)
            {
            }
        }

        void SetDialogError(Error error)
        {
            dialogError ??= error;
            CloseDialog();
        }

        using var cancellationRegistration = ct.Register(() =>
        {
            SetDialogError(new Error
            {
                Code = "AUTH_WEBVIEW_LOGIN_CANCELLED",
                UserMessage = "已取消登录",
                TechnicalMessage = "Embedded login dialog was cancelled by cancellation token.",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        });

        async Task InitializeWebViewAsync()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();

                var coreWebView = webView.CoreWebView2;
                if (coreWebView is null)
                {
                    SetDialogError(new Error
                    {
                        Code = "AUTH_WEBVIEW_INIT_FAILED",
                        UserMessage = "嵌入式登录初始化失败，请重试",
                        TechnicalMessage = "WebView2 CoreWebView2 instance is null after EnsureCoreWebView2Async.",
                        CanRetry = true,
                        Severity = ErrorSeverity.Error,
                    });
                    return;
                }

                coreWebView.Settings.IsStatusBarEnabled = false;
                coreWebView.Settings.IsZoomControlEnabled = false;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;

                if (!string.IsNullOrWhiteSpace(loginContext.UserAgent))
                {
                    try
                    {
                        await coreWebView.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
                        await coreWebView.CallDevToolsProtocolMethodAsync(
                            "Network.setUserAgentOverride",
                            JsonSerializer.Serialize(new { userAgent = loginContext.UserAgent }));
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "设置嵌入式登录 User-Agent 失败，将继续尝试默认 User-Agent");
                    }
                }

                await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(EpicLoginWebViewBridge.BootstrapScript);

                coreWebView.WebMessageReceived += (_, args) =>
                {
                    if (!EpicLoginWebViewBridge.TryParseMessage(args.TryGetWebMessageAsString(), out var message) || message is null)
                    {
                        return;
                    }

                    if (string.Equals(message.Type, "exchange_code", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(message.ExchangeCode))
                        {
                            Logger.Warning("嵌入式登录桥接收到了空 exchange code");
                            return;
                        }

                        exchangeCode = message.ExchangeCode.Trim();
                        Logger.Information("嵌入式登录已捕获 exchange code，准备完成登录");
                        CloseDialog();
                        return;
                    }

                    if (string.Equals(message.Type, "launch_external_url", StringComparison.OrdinalIgnoreCase)
                        && Uri.TryCreate(message.Url, UriKind.Absolute, out var externalUri))
                    {
                        Logger.Information("嵌入式登录请求打开外部链接 | Url={Url}", externalUri);
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = externalUri.AbsoluteUri,
                                UseShellExecute = true,
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "打开嵌入式登录外部链接失败");
                        }
                    }
                };

                coreWebView.NavigationStarting += (_, args) =>
                {
                    progressRing.IsActive = true;
                    statusText.Text = "正在连接 Epic 登录服务...";
                    Logger.Debug("嵌入式登录开始导航 | Uri={Uri}", args.Uri);
                };

                coreWebView.NavigationCompleted += (_, args) =>
                {
                    progressRing.IsActive = false;

                    if (!args.IsSuccess)
                    {
                        SetDialogError(new Error
                        {
                            Code = "AUTH_WEBVIEW_NAVIGATION_FAILED",
                            UserMessage = "嵌入式登录页面加载失败，请重试",
                            TechnicalMessage = $"WebView2 navigation failed with status '{args.WebErrorStatus}'.",
                            CanRetry = true,
                            Severity = ErrorSeverity.Warning,
                        });
                        return;
                    }

                    statusText.Text = "请继续在此窗口中完成 Epic 登录。应用不会要求你手动输入敏感信息。";
                };

                coreWebView.Navigate(loginContext.LoginUrl);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "初始化嵌入式登录 WebView2 失败");
                SetDialogError(new Error
                {
                    Code = "AUTH_WEBVIEW_INIT_EXCEPTION",
                    UserMessage = "嵌入式登录初始化失败，请重试",
                    TechnicalMessage = ex.Message,
                    CanRetry = true,
                    Severity = ErrorSeverity.Error,
                });
            }
        }

        _ = InitializeWebViewAsync();

        var result = await dialog.ShowAsync();
        if (!string.IsNullOrWhiteSpace(exchangeCode))
        {
            return Result.Ok(exchangeCode);
        }

        if (dialogError is not null)
        {
            return Result.Fail<string>(dialogError);
        }

        if (result == ContentDialogResult.None)
        {
            return Result.Fail<string>(new Error
            {
                Code = "AUTH_WEBVIEW_LOGIN_CANCELLED",
                UserMessage = "已取消登录",
                TechnicalMessage = "User closed embedded Epic login dialog.",
                CanRetry = true,
                Severity = ErrorSeverity.Warning,
            });
        }

        return Result.Fail<string>(new Error
        {
            Code = "AUTH_WEBVIEW_LOGIN_FAILED",
            UserMessage = "嵌入式登录未完成，请重试",
            TechnicalMessage = "Embedded Epic login dialog completed without exchange code.",
            CanRetry = true,
            Severity = ErrorSeverity.Warning,
        });
    }

    public Task<TResult?> ShowCustomAsync<TResult>(object dialogViewModel)
    {
        // 自定义对话框将在后续任务中实现
        Logger.Warning("ShowCustomAsync 尚未实现");
        return Task.FromResult<TResult?>(default);
    }
}

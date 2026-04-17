// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.Auth.Contracts;
using Launcher.Shared;

namespace Launcher.Infrastructure.Auth;

/// <summary>
/// Auth 模块内部 grant 执行器。根据归一化后的登录结果执行 token exchange。
/// </summary>
internal interface IEpicLoginGrantExecutor
{
    string GrantType { get; }

    bool CanExecute(EpicLoginResultKind kind);

    Task<Result<TokenPair>> ExecuteAsync(EpicLoginResult input, CancellationToken ct);
}
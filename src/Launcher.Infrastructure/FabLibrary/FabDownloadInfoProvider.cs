// Copyright (c) Helsincy. All rights reserved.

using Launcher.Application.Modules.FabLibrary.Contracts;
using Launcher.Shared;
using Serilog;

namespace Launcher.Infrastructure.FabLibrary;

/// <summary>
/// IFabDownloadInfoProvider 的实现。委托给 FabApiClient 并将内部 DTO 映射为公共模型。
/// </summary>
internal sealed class FabDownloadInfoProvider : IFabDownloadInfoProvider
{
    private readonly FabApiClient _fabApiClient;
    private readonly ILogger _logger = Log.ForContext<FabDownloadInfoProvider>();

    public FabDownloadInfoProvider(FabApiClient fabApiClient)
    {
        _fabApiClient = fabApiClient;
    }

    public async Task<Result<FabDownloadInfo>> GetDownloadInfoAsync(string assetId, CancellationToken ct)
    {
        var result = await _fabApiClient.GetDownloadInfoAsync(assetId, ct);
        if (!result.IsSuccess)
        {
            _logger.Warning("获取下载信息失败 {AssetId}: {Error}", assetId, result.Error?.TechnicalMessage);
            return Result.Fail<FabDownloadInfo>(result.Error!);
        }

        var dto = result.Value!;
        return Result.Ok(new FabDownloadInfo
        {
            AssetId = dto.AssetId,
            DownloadUrl = dto.DownloadUrl,
            FileName = dto.FileName,
            FileSize = dto.FileSize,
            Version = dto.Version,
        });
    }
}

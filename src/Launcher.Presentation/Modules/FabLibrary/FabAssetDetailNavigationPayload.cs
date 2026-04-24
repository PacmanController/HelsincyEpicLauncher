// Copyright (c) Helsincy. All rights reserved.

namespace Launcher.Presentation.Modules.FabLibrary;

/// <summary>
/// Fab 详情页导航载荷。用于从列表页向详情页透传稳定的 preview 锚点。
/// </summary>
public sealed record FabAssetDetailNavigationPayload(
    string AssetId,
    string PreviewListingId,
    string PreviewProductId);
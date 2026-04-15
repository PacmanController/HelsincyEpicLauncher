# 第2遍审查：模块耦合与依赖

> 审查人：AI Agent  
> 日期：2026-04-16  
> 基线文档：04-ModuleDependencyRules.md, 14-AntiPatterns.md  
> 前序审查：01-Review-ArchitectureCompliance.md

---

## 审查范围

验证模块间通信方式是否符合 04-ModuleDependencyRules.md 中的禁止项（P-01 ~ P-06），反模式检查（AP-01 ~ AP-06），以及模块依赖表的合规检查。

---

## 一、禁止项检查

### P-01：跨模块引用内部实现

#### 发现 R2-01（🔴 严重）

**文件**：`Infrastructure/Installations/RepairDownloadUrlProvider.cs` L4, L17

```csharp
using Launcher.Infrastructure.FabLibrary;    // ← 跨模块内部引用！

public sealed class RepairDownloadUrlProvider : IRepairDownloadUrlProvider
{
    private readonly FabApiClient _fabApiClient;  // ← 直接依赖具体类
```

**问题**：`Installations` 模块的 `RepairDownloadUrlProvider` 直接引用了 `FabLibrary` 模块的 Infrastructure 内部类 `FabApiClient`。这完全违反了 P-01 规则——"跨模块引用内部实现"是硬性禁止的。

**文档原文**：
> ❌ FabLibrary → Downloads.Infrastructure.ChunkDownloadClient  
> ✅ 通过 Downloads.Contracts.IDownloadReadService 查询

**影响**：`FabApiClient` 的任何 API 变化会导致 `Installations` 模块也必须修改，模块边界被穿透。

**修复建议**：在 `Launcher.Application.Modules.FabLibrary.Contracts` 中定义 `IFabDownloadInfoProvider` 接口，让 `RepairDownloadUrlProvider` 依赖该接口而非具体类 `FabApiClient`。

---

### P-02：跨模块直接操作 ViewModel ✅ 通过

未发现任何模块直接引用其他模块的 ViewModel。所有跨模块通信通过 Contracts 接口或事件。

### P-03：跨模块共享可变领域对象 ✅ 通过

跨模块数据传输使用不可变 DTO/record（`init` 属性、`readonly` 字段）。未发现共享可变对象实例。

### P-04：模块绕过 Contracts 直连数据库 ✅ 通过

所有数据库操作通过 Repository 接口。SQLite 连接通过 `IDbConnectionFactory` 统一管理。

### P-05：反向依赖 ✅ 通过

| 检查项 | 结果 |
|--------|------|
| Infrastructure → Presentation | 未发现 ✅ |
| Domain → Infrastructure | 未发现 ✅ |
| Application → Presentation | 未发现 ✅ |
| Background → Infrastructure | 未发现 ✅ |

### P-06：循环依赖 ✅ 通过

无循环依赖。所有依赖方向为单向。

---

## 二、模块依赖表合规性

### 文档定义的依赖表

| 源模块 | 允许依赖的 Contracts |
|--------|---------------------|
| Shell | 所有模块的 Contracts |
| FabLibrary | Downloads.Contracts, Installations.Contracts |
| Downloads | 无跨模块依赖 |
| Installations | Downloads.Contracts |
| EngineVersions | Downloads.Contracts, Installations.Contracts |
| Plugins | FabLibrary.Contracts, Installations.Contracts |
| Settings | 无跨模块依赖 |
| Diagnostics | Settings.Contracts（文档中提到） |
| Updates | Settings.Contracts（文档中提到） |

### 实际跨模块依赖校验

#### 发现 R2-02（🟡 中等）— Plugins → EngineVersions.Contracts 超出依赖表

**文件**：`Infrastructure/Plugins/PluginReadService.cs` L4, L21

```csharp
using Launcher.Application.Modules.EngineVersions.Contracts;

private readonly IEngineVersionReadService _engineVersionReadService;
```

**问题**：依赖表规定 `Plugins` 仅可依赖 `FabLibrary.Contracts` 和 `Installations.Contracts`，但实际依赖了 `EngineVersions.Contracts` 用于引擎版本兼容性检查。

**评估**：业务上合理——插件兼容性检查确实需要引擎版本信息（06-ModuleDefinitions/Plugins.md 的 API 中就有 `CheckCompatibilityAsync(pluginId, engineVersionId)` 方法）。

**修复建议**：更新 `04-ModuleDependencyRules.md` 依赖表，将 `EngineVersions.Contracts` 列入 `Plugins` 的允许依赖。

---

#### 发现 R2-03（🟡 中等）— FabLibrary → Auth.Contracts 超出依赖表

**文件**：`Infrastructure/FabLibrary/FabApiClient.cs` L7

```csharp
using Launcher.Application.Modules.Auth.Contracts;
```

**问题**：依赖表规定 `FabLibrary` 允许依赖 `Downloads.Contracts` 和 `Installations.Contracts`，但 `FabApiClient` 实际直接注入 `IAuthService` 获取 Access Token 以调用 Fab API。

**评估**：业务上完全合理——Fab API 需要认证 Token。`06-ModuleDefinitions/FabLibrary.md` 的依赖表中确实列出了 `Auth.Contracts` 作为依赖（"获取 Token 调用 Fab API"），但 `04-ModuleDependencyRules.md` 的总表未列出此项。这属于**两份文档不一致**。

**修复建议**：
1. 方案 A：更新 `04-ModuleDependencyRules.md` 总表，明确列出所有需要 API 认证的模块对 `Auth.Contracts` 的依赖
2. 方案 B（更优）：通过 `HttpClient` 的 `DelegatingHandler` 统一注入 Auth Token，消除各模块对 `IAuthService` 的显式依赖

---

#### 发现 R2-04（🟡 中等）— EngineVersions → Auth.Contracts 超出依赖表

**文件**：`Infrastructure/EngineVersions/EngineVersionApiClient.cs` L7

```csharp
using Launcher.Application.Modules.Auth.Contracts;
```

**问题**：同 R2-03。依赖表规定 `EngineVersions` 允许 `Downloads.Contracts` 和 `Installations.Contracts`，但实际依赖了 `Auth.Contracts`。

**评估**：与 R2-03 完全相同的模式。`06-ModuleDefinitions/EngineVersions.md` 中也列出了 `Auth.Contracts`。

**修复建议**：同 R2-03。

---

#### 发现 R2-05（🟡 中等）— Contracts 层跨模块类型泄漏

**文件**：`Application/Modules/FabLibrary/Contracts/IFabAssetCommandService.cs` L3, L15

```csharp
using Launcher.Domain.Downloads;

public interface IFabAssetCommandService
{
    Task<Result<DownloadTaskId>> DownloadAssetAsync(string assetId, string installPath, CancellationToken ct);
}
```

**问题**：`FabLibrary` 的 Contracts 接口返回值使用了 `Downloads` 模块的 Domain 类型 `DownloadTaskId`。Contracts 应该是自包含的模块边界。此处将 Downloads 的内部类型暴露到了 FabLibrary 的公共契约中。任何依赖 `IFabAssetCommandService` 的消费方也被迫传递依赖 `Downloads.Domain`。

**修复建议**：
1. 返回 `Result<Guid>` 替代 `Result<DownloadTaskId>`（消除跨模块类型依赖）
2. 或在 `FabLibrary.Contracts` 中定义自己的 wrapper 类型

---

## 三、反模式检查

### AP-01：God Service ✅ 通过

最大的 Service（按公共方法数）：

| Service | 公共方法数 | 阈值 | 状态 |
|---------|-----------|------|------|
| AppUpdateService | 8 | 15 | ✅ |
| DownloadOrchestrator | 8 | 15 | ✅ |
| AuthService | 7 | 15 | ✅ |
| SettingsService | 7 | 15 | ✅ |

### AP-02：Page Code-Behind 包含业务逻辑 ✅ 通过

（已在第1遍审查中确认）

### AP-03：ViewModel > 400 行 ✅ 通过

| ViewModel | 行数 | 状态 |
|-----------|------|------|
| FabLibraryViewModel | 342 | ✅ |
| DiagnosticsViewModel | 327 | ✅ |
| ShellViewModel | 321 | ✅ |
| DownloadsViewModel | 293 | ✅ |
| SettingsViewModel | 260 | ✅ |
| FabAssetDetailViewModel | 254 | ✅ |
| EngineVersionsViewModel | 240 | ✅ |
| InstallationsViewModel | 233 | ✅ |
| PluginsViewModel | 124 | ✅ |

最大 342 行，均在 400 行以内。

### AP-04：跨模块共享可变对象 ✅ 通过

同 P-03 检查。

### AP-05：全局静态可变状态 ✅ 通过

所有 `static` 字段均为 `readonly`：
- `ILogger` 实例
- `TimeSpan` 常量
- `JsonSerializerOptions` 常量
- `IComparer` 实例

`App.Services` 和 `ViewModelLocator._serviceProvider` 是组合根设施，为 `private` 或 `internal`，非公开可变全局状态。

### AP-06：下载进度高频 UI 刷新

#### 发现 R2-06（🟢 轻微）

**文件**：
- `Presentation/Modules/Downloads/DownloadsViewModel.cs` — `OnSnapshotChanged` 无 ViewModel 侧节流
- `Presentation/Shell/ShellViewModel.cs` — `OnDownloadSnapshotChanged` 无 ViewModel 侧节流

**当前保护**：`DownloadRuntimeStore.cs` 在源头实现了 500ms 节流（`SpeedCalculator.ShouldNotify()`）。

**风险**：节流保护位于 Infrastructure 实现层，非契约约束。若有其他 `IDownloadRuntimeStore` 实现不包含节流，UI 会受到高频刷新冲击。

**建议**：在 ViewModel 层添加防御性节流（如 `Stopwatch` 或 `Timer` 限制最多每 500ms 更新一次 UI）。

---

## 四、DI 注册交叉检查

### 发现 R2-07（🟢 轻微）— ViewModelLocator 静态服务定位器

**文件**：`Presentation/ViewModelLocator.cs` L13

```csharp
private static IServiceProvider? _serviceProvider;
```

**评估**：代码有清晰注释说明这是 WinUI `Frame.Navigate` 无法构造函数注入的变通方案。字段为 `private`，仅通过 `Configure()` 方法一次性赋值。属于 WinUI 平台限制下的合理变通。

**建议**：在文档中记录这一架构妥协，确保后续维护者了解原因。

---

## 审查总结

| 类别 | 通过 | 问题 |
|------|------|------|
| P-01 跨模块内部引用 | ❌ | 1 🔴 (RepairDownloadUrlProvider → FabApiClient) |
| P-02 跨模块 ViewModel 操作 | ✅ | 0 |
| P-03 跨模块共享可变对象 | ✅ | 0 |
| P-04 绕过 Contracts 查数据库 | ✅ | 0 |
| P-05 反向依赖 | ✅ | 0 |
| P-06 循环依赖 | ✅ | 0 |
| 依赖表合规 | ❌ | 3 🟡 (Auth.Contracts 未列入 + Plugins→EngineVersions) |
| Contracts 类型泄漏 | ❌ | 1 🟡 (DownloadTaskId 在 FabLibrary.Contracts) |
| 反模式 AP-01~AP-05 | ✅ | 0 |
| AP-06 进度刷新 | ⚠️ | 1 🟢 |
| DI 注册 | ⚠️ | 1 🟢 |

### 发现汇总

| ID | 严重度 | 位置 | 摘要 |
|----|--------|------|------|
| R2-01 | 🔴 | RepairDownloadUrlProvider.cs | 跨模块直接引用 FabApiClient 具体类（违反 P-01） |
| R2-02 | 🟡 | PluginReadService.cs | Plugins → EngineVersions.Contracts 超出依赖表 |
| R2-03 | 🟡 | FabApiClient.cs | FabLibrary → Auth.Contracts 超出总依赖表（模块文档有列但总表未列） |
| R2-04 | 🟡 | EngineVersionApiClient.cs | EngineVersions → Auth.Contracts 超出总依赖表（同上） |
| R2-05 | 🟡 | IFabAssetCommandService.cs | Contracts 返回值使用跨模块 Domain 类型 DownloadTaskId |
| R2-06 | 🟢 | DownloadsViewModel/ShellViewModel | ViewModel 层无防御性进度节流 |
| R2-07 | 🟢 | ViewModelLocator.cs | 静态服务定位器（WinUI 限制下的合理变通） |

**总计**：7 个问题（1 🔴 严重 + 4 🟡 中等 + 2 🟢 轻微）

### 累计发现（第1+2遍）

| 统计 | 数量 |
|------|------|
| 🔴 严重 | 2 |
| 🟡 中等 | 8 |
| 🟢 轻微 | 3 |
| **合计** | **13** |

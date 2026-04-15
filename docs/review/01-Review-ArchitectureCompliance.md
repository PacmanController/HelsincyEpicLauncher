# 第1遍审查：架构与分层合规性

> 审查人：AI Agent  
> 日期：2026-04-16  
> 基线文档：02-ArchitecturePrinciples.md, 03-SolutionStructure.md, 04-ModuleDependencyRules.md, 10-StartupPipeline.md

---

## 审查范围

验证项目六层架构（App / Presentation / Application / Domain / Infrastructure / Background / Shared）边界是否清晰，是否严格遵守文档定义的引用关系和架构原则。

---

## 一、项目引用合规性（csproj 分析）

### 文档定义的引用关系

```
App → Presentation, Infrastructure, Background
Presentation → Application, Domain(仅Contracts/DTO/枚举), Shared
Application → Domain, Shared
Domain → Shared (零外部依赖)
Infrastructure → Application, Domain, Shared
Background → Application, Domain, Shared (禁止引用 Infrastructure)
```

### 实际引用 vs 文档

| 项目 | 实际引用 | 文档要求 | 结果 |
|------|----------|----------|------|
| Launcher.App | Presentation, Infrastructure, Background | Presentation, Infrastructure, Background | ✅ 完全匹配 |
| Launcher.Presentation | Application, Shared | Application, Domain(仅Contracts), Shared | ✅ 合规（比文档更严格，未直接引用 Domain） |
| Launcher.Application | Domain, Shared | Domain, Shared | ✅ 完全匹配 |
| Launcher.Domain | Shared | Shared | ✅ 完全匹配 |
| Launcher.Infrastructure | Application, Domain, Shared | Application, Domain, Shared | ✅ 完全匹配 |
| Launcher.Background | Application, Domain, Shared | Application, Domain, Shared (禁止 Infrastructure) | ✅ 完全匹配 |

### 传递引用使用

Presentation 虽未直接引用 Domain，但通过 Application → Domain 的传递引用使用了以下 Domain 类型：
- `Launcher.Domain.Downloads.DownloadTaskId`（DownloadsPage.xaml.cs, DownloadsViewModel.cs）
- `Launcher.Domain.Installations.InstallState`（InstallationsViewModel.cs）

**评估**：这些均为值类型标识符和枚举，符合文档 "仅 Contracts/DTO/枚举" 的限制。✅ 合规

---

## 二、原则1 — UI 只负责显示和交互

### 逐文件审查

| 文件 | 结果 | 备注 |
|------|------|------|
| ShellPage.xaml.cs | ✅ | 仅注入 ViewModel + 服务初始化 + 导航事件路由 |
| FabLibraryPage.xaml.cs | 🟢 轻微 | 包含 SortComboBox 的 string→enum 映射和滚动加载阈值判断（200px），严格来讲可挪到 ViewModel |
| FabAssetDetailPage.xaml.cs | ✅ | 仅提取导航参数调用 ViewModel Command |
| DownloadsPage.xaml.cs | ✅ | 按钮事件提取 Tag → 调用 ViewModel Command |
| InstallationsPage.xaml.cs | ✅ | 所有按钮事件委托 ViewModel；附带 x:Bind 静态转换器方法（纯视觉辅助） |
| EngineVersionsPage.xaml.cs | ✅ | 按钮事件提取 Tag 调用 ViewModel Command |
| PluginsPage.xaml.cs | ✅ | Page_Loaded / Refresh_Click 调用 ViewModel.LoadCommand |
| DiagnosticsPage.xaml.cs | ✅ | Page_Loaded 调用 RefreshSystemInfoCommand |
| SettingsPage.xaml.cs | ✅ | 主题切换映射为 ElementTheme 枚举（纯 UI 映射） |
| MainWindow.xaml.cs | ✅ | 窗口配置（标题栏、Mica、尺寸限制）|

### 发现 R1-01

| ID | 严重度 | 文件 | 描述 |
|----|--------|------|------|
| R1-01 | 🟢 轻微 | `Presentation/Modules/FabLibrary/FabLibraryPage.xaml.cs` L56-66 | `SortComboBox_SelectionChanged` 内含 `string → FabSortOrder` switch 映射。虽属 UI 值转换但可提取到 ViewModel 的 SortOrder setter 逻辑中 |

---

## 三、原则2 — ViewModel 不含业务逻辑

### 逐文件审查

| 文件 | 结果 | 备注 |
|------|------|------|
| ShellViewModel.cs | 🟡 | 使用 `Microsoft.UI.Xaml.Visibility` 作为属性返回类型，耦合 WinUI 框架 |
| FabLibraryViewModel.cs | ✅ | 搜索防抖/分页为 UI 状态管理，所有数据操作通过接口注入 |
| FabAssetDetailViewModel.cs | 🟡 | `Path.Combine + Environment.GetFolderPath` 硬编码安装路径 |
| DownloadsViewModel.cs | ✅ | 事件驱动更新，所有操作通过契约接口 |
| InstallationsViewModel.cs | ✅ | 所有操作通过接口 |
| EngineVersionsViewModel.cs | 🟡 | `Path.Combine + Environment.SpecialFolder.ProgramFiles` 硬编码安装路径 |
| PluginsViewModel.cs | ✅ | 所有操作通过接口 |
| SettingsViewModel.cs | ✅ | 通过 ISettingsReadService/ISettingsCommandService |
| DiagnosticsViewModel.cs | ✅ | 通过 IDiagnosticsReadService/ICacheManager |

### 发现 R1-02 ~ R1-04

| ID | 严重度 | 文件 | 行号 | 描述 |
|----|--------|------|------|------|
| R1-02 | 🟡 中等 | `Presentation/Shell/ShellViewModel.cs` | L68-69 | `CanSkipUpdate` 属性返回 `Microsoft.UI.Xaml.Visibility` 类型，直接耦合了 WinUI 框架类型。ViewModel 应使用 `bool` 类型，由 XAML 侧使用 `BoolToVisibilityConverter` 转换。违反 ViewModel 与 UI 框架解耦原则 |
| R1-03 | 🟡 中等 | `Presentation/Modules/EngineVersions/EngineVersionsViewModel.cs` | L116-119 | `DownloadAsync` 中使用 `Path.Combine(Environment.GetFolderPath(SpecialFolder.ProgramFiles), "Epic Games", $"UE_{item.DisplayName}")` 硬编码安装路径。路径构建策略应由 Application/Infrastructure 层（通过 `ISettingsReadService` 或 `IAppConfigProvider`）提供，而非在 ViewModel 中直接决定 |
| R1-04 | 🟡 中等 | `Presentation/Modules/FabLibrary/FabAssetDetailViewModel.cs` | L141-143 | `DownloadAsync` 中使用 `Path.Combine(Environment.GetFolderPath(SpecialFolder.LocalApplicationData), "Helsincy", "EpicLauncher", "Assets", AssetId)` 硬编码安装路径。同 R1-03，应通过配置服务获取 |

---

## 四、Presentation 层 Shell 服务审查

### 发现 R1-05（严重）

| ID | 严重度 | 文件 | 行号 | 描述 |
|----|--------|------|------|------|
| R1-05 | 🔴 严重 | `Presentation/Shell/ThemeService.cs` | L68-105 | **Presentation 层直接执行文件 I/O 操作**。`LoadTheme()` 调用 `File.Exists()` + `File.ReadAllText()`；`SaveTheme()` 调用 `Directory.Exists()` + `Directory.CreateDirectory()` + `File.WriteAllText()`。根据 02-ArchitecturePrinciples.md 原则 1-2，Presentation 层禁止直接进行文件系统操作。主题持久化应委托给 Infrastructure 层实现（例如通过 `ISettingsCommandService` 扩展或创建 `IThemePersistenceService` 接口）|

---

## 五、DI 注册合规性

### 各层 DependencyInjection.cs 审查

| 文件 | 结果 | 备注 |
|------|------|------|
| Domain/DependencyInjection.cs | ✅ | 空占位（Domain 无需注册服务） |
| Application/DependencyInjection.cs | ✅ | 空占位（Application 契约由 Infrastructure 实现） |
| Presentation/DependencyInjection.cs | ✅ | 注册 NavigationService/NotificationService/DialogService/ThemeService/ViewModels |
| Infrastructure/DependencyInjection.cs | ✅ | 实现所有 Application 层契约接口 + HttpClient 工厂模式 |
| Background/DependencyInjection.cs | ✅ | 4 个 Worker 全部 Singleton，无 Infrastructure 引用 |

**所有 DI 注册集中在 App.xaml.cs 的 `InitializeCoreServices()` 中统一调用**：✅ 合规

注册顺序：`AddDomain()` → `AddApplication()` → `AddInfrastructure()` → `AddPresentation()` → `AddBackground()`

---

## 六、启动管线合规性（vs 10-StartupPipeline.md）

### 文档要求的启动阶段

| 阶段 | 文档要求 | 实际实现 | 合规 |
|------|----------|----------|------|
| Phase 0（<500ms） | 单实例检查 → MainWindow → 骨架屏 | ✅ Mutex + 管道 → MainWindow | ✅ |
| Phase 1（500ms-1.5s） | DI + 配置 + 日志 + SQLite + Shell + 导航 | ✅ `InitializeCoreServices()` 含 OperationTimer | ✅ |
| Phase 2（后台恢复） | 会话恢复 + 下载恢复 + 索引加载 | ⚠️ 未看到显式的 Phase 2 独立步骤 | 见 R1-06 |
| Phase 3（延迟初始化） | 目录刷新 + 缩略图预热 + 更新检查 | ✅ `Task.Run(StartBackgroundServicesAsync)` | ✅ |

### 发现 R1-06

| ID | 严重度 | 文件 | 描述 |
|----|--------|------|------|
| R1-06 | 🟡 中等 | `App/App.xaml.cs` | **Phase 2（后台恢复）未独立实现**。文档 10-StartupPipeline.md 定义 Phase 2 包含：会话恢复 `IAuthService.TryRestoreSessionAsync()`、下载恢复 `IDownloadOrchestrator.RecoverAsync()`、已安装资产索引加载。实际代码中 Phase 2 合并到了 Phase 3 的后台服务启动中（通过各 Worker 的 Start() 间接完成），而非作为独立的恢复步骤。这导致恢复逻辑分散在各 Worker 中，不如文档设计的集中恢复清晰 |

---

## 七、Background 层隔离验证

使用 grep 在 Background 项目中搜索 `using Launcher.Infrastructure`：**0 匹配**

Background 层仅使用以下命名空间：
- `Launcher.Application.Modules.*.Contracts` — 通过契约接口操作
- `Launcher.Domain.*` — 领域枚举/值对象
- `Launcher.Shared.*` — 基础类型
- `Serilog` — 日志

**结论**：✅ Background 层与 Infrastructure 层完全隔离，严格遵守 AI-03 规则

---

## 八、Domain 层纯净性验证

Domain 层文件清单：
- `Common/Entity.cs` — 基础实体类
- `Common/StateMachine.cs` — 通用状态机基类
- `Downloads/DownloadCheckpoint.cs` — 断点数据模型
- `Downloads/DownloadState.cs` — 下载状态枚举
- `Downloads/DownloadStateMachine.cs` — 下载状态机（继承 StateMachine）
- `Downloads/DownloadTask.cs` — 下载任务领域实体
- `Downloads/DownloadTaskId.cs` — 强类型 ID（record struct）
- `Installations/Installation.cs` — 安装领域实体
- `Installations/InstallManifest.cs` — 安装清单模型
- `Installations/InstallState.cs` — 安装状态枚举
- `Installations/InstallStateMachine.cs` — 安装状态机

所有文件仅引用 `Launcher.Shared`，无 `Launcher.Application`/`Infrastructure`/`Presentation`/`Background` 引用。

**结论**：✅ Domain 层零外部依赖，纯业务规则

---

## 审查总结

| 类别 | 通过 | 问题 |
|------|------|------|
| 层级引用（csproj） | 6/6 ✅ | 0 |
| Code-Behind 合规 | 9/10 | 1 🟢 |
| ViewModel 合规 | 6/9 | 3 🟡 |
| Shell 服务合规 | 0/1 | 1 🔴 |
| DI 注册合规 | 5/5 ✅ | 0 |
| 启动管线合规 | 3/4 | 1 🟡 |
| Background 隔离 | ✅ | 0 |
| Domain 纯净性 | ✅ | 0 |

### 发现汇总

| ID | 严重度 | 位置 | 摘要 |
|----|--------|------|------|
| R1-01 | 🟢 | FabLibraryPage.xaml.cs | 排序映射可提取到 ViewModel |
| R1-02 | 🟡 | ShellViewModel.cs | `CanSkipUpdate` 返回 `Visibility` 类型，耦合 WinUI |
| R1-03 | 🟡 | EngineVersionsViewModel.cs | 硬编码引擎安装路径 |
| R1-04 | 🟡 | FabAssetDetailViewModel.cs | 硬编码资产安装路径 |
| R1-05 | 🔴 | ThemeService.cs | Presentation 层直接做文件 I/O |
| R1-06 | 🟡 | App.xaml.cs | Phase 2 恢复步骤未独立实现 |

**总计**：6 个问题（1 🔴 严重 + 4 🟡 中等 + 1 🟢 轻微）

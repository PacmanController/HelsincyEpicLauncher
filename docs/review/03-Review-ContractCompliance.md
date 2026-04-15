# 第3遍审查：文档契约 vs 实际实现

> 审查人：AI Agent  
> 日期：2026-04-16  
> 基线文档：05-CoreInterfaces.md, 07-DownloadSubsystem.md, 08-StateManagement.md, 09-ErrorHandling.md, 10-StartupPipeline.md, 15-LoggingStrategy.md  
> 前序审查：01-Review-ArchitectureCompliance.md, 02-Review-ModuleCoupling.md

---

## 审查范围

逐项对照文档定义的接口签名、状态机、错误处理模式、启动流程、日志策略、状态管理与实际代码进行合规性审查。

---

## 一、核心接口签名合规性

### 1.1 完全匹配的接口

| 接口 | 文档来源 | 代码文件 | 状态 |
|------|----------|----------|------|
| INavigationService | 05-CoreInterfaces §1 | `Presentation/Shell/Navigation/INavigationService.cs` | ✅ 完全匹配 |
| IDialogService | 05-CoreInterfaces §2 | `Presentation/Shell/IDialogService.cs` | ✅ 完全匹配 |
| INotificationService | 05-CoreInterfaces §3 | `Presentation/Shell/INotificationService.cs` | ✅ 完全匹配 |

### 1.2 有差异的接口

#### 发现 R3-01（🟡 中等）— IAuthService 新增未文档化的事件

**文件**：`Application/Modules/Auth/Contracts/IAuthService.cs`

| 成员 | 文档定义 | 实际代码 |
|------|----------|----------|
| SessionExpired 事件 | 未定义 | `event Action<SessionExpiredEvent>? SessionExpired;` |
| CancellationToken | 必需参数 | 可选参数 `ct = default` |

**评估**：`SessionExpired` 事件在 Auth 模块流程文档（06-ModuleDefinitions/Auth.md）中有描述（"发布 SessionExpiredEvent"），但 05-CoreInterfaces.md 的接口定义中缺失。属于**文档未更新**。

---

#### 发现 R3-02（🟡 中等）— IDownloadCommandService 新增 PauseAllAsync/ResumeAllAsync

**文件**：`Application/Modules/Downloads/Contracts/IDownloadCommandService.cs` L22, L28

```csharp
Task<Result> PauseAllAsync(CancellationToken ct = default);
Task<Result> ResumeAllAsync(CancellationToken ct = default);
```

文档 05-CoreInterfaces.md 仅定义 5 个方法（StartAsync, PauseAsync, ResumeAsync, CancelAsync, SetPriorityAsync）。`PauseAllAsync` / `ResumeAllAsync` 是 Phase 8 网络韧性功能新增，**文档未同步更新**。

---

#### 发现 R3-03（🔴 严重）— IDownloadOrchestrator 接口不存在

**文档定义**：05-CoreInterfaces.md §5.3 和 07-DownloadSubsystem.md 均定义了 `IDownloadOrchestrator` 接口，含 `EnqueueAsync`, `PauseAsync`, `ResumeAsync`, `CancelAsync`, `RecoverAsync` 五个方法。

**实际代码**：`Infrastructure/Downloads/DownloadOrchestrator.cs` 是**具体类**，直接在 DI 中注册。项目中不存在 `IDownloadOrchestrator.cs` 接口文件。

**违反原则**：
- 文档要求"编排器由 Application 层 Handler 调用"，通过接口隔离
- 缺少接口意味着下载编排器无法被 Mock 测试
- 违反依赖倒置原则（DIP）

**涉及文档**：03-SolutionStructure.md L346 也列出了 `IDownloadOrchestrator.cs`

---

#### 发现 R3-04（🟡 中等）— IDownloadScheduler / IChunkDownloader 接口不存在

**文档定义**：05-CoreInterfaces.md 定义了 `IDownloadScheduler` 和 `IChunkDownloader` 接口。

**实际代码**：
- `DownloadScheduler.cs` — 具体类，无接口
- `ChunkDownloadClient.cs` — 具体类，无接口

**评估**：这两个是下载子系统内部组件，不对外暴露，影响程度低于 IDownloadOrchestrator。但是缺少接口仍然影响单元测试能力。

---

#### 发现 R3-05（🟡 中等）— IDownloadCheckpointRepository 被合并

**文档定义**：07-DownloadSubsystem.md 将 `IDownloadCheckpointRepository` 定义为独立接口。

**实际代码**：Checkpoint 方法（`SaveCheckpointAsync`, `GetCheckpointAsync`, `DeleteCheckpointAsync`）被合并进 `IDownloadTaskRepository`。

**评估**：合并后接口职责增大，但减少了接口数量。功能等价但不符合文档设计。

---

#### 发现 R3-06（🟡 中等）— IDownloadRuntimeStore 签名大幅偏离

**文件**：`Application/Modules/Downloads/Contracts/IDownloadRuntimeStore.cs`

| 文档定义 (08-StateManagement.md) | 实际代码 |
|----------------------------------|----------|
| `IReadOnlyCollection<DownloadRuntimeSnapshot> Current` | ❌ 不存在 |
| `void Upsert(DownloadRuntimeSnapshot snapshot)` | ❌ 不存在 |
| `void Remove(DownloadTaskId taskId)` | ❌ 不存在 |
| `event EventHandler<DownloadRuntimeSnapshot>? SnapshotChanged` | `event Action<DownloadProgressSnapshot>?` — 委托类型 + DTO 名均不同 |
| — | 新增：`DownloadCompleted` 事件 |
| — | 新增：`DownloadFailed` 事件 |
| — | 新增：`GetSnapshot(taskId)` 方法 |
| — | 新增：`GetAllSnapshots()` 方法 |

**DTO 名称不同**：文档用 `DownloadRuntimeSnapshot`，代码用 `DownloadProgressSnapshot`。

**评估**：实际实现比文档更丰富，功能性更强，但偏离了文档设计。

---

#### 发现 R3-07（🟢 轻微）— DownloadUiState 枚举缺少 Installing 值

**文件**：`Domain/Downloads/DownloadState.cs`

文档定义的 `DownloadUiState`：`Queued, Downloading, Paused, Verifying, Installing, Completed, Failed, Cancelled`

实际代码：`Installing` 值不存在。安装由 Installations 模块独立管理，下载 UI 状态不需要 Installing。

---

### 1.3 CancellationToken 可选参数

多个接口将文档定义的 `CancellationToken ct`（必需参数）改为 `ct = default`（可选参数）。涉及：
- IAuthService
- IDownloadReadService  
- IDownloadCommandService
- IFabCatalogReadService

**评估**：🟢 轻微差异，向后兼容，实际使用更方便。

---

## 二、下载状态机合规性

### 2.1 状态枚举 ✅ 完全匹配

`Domain/Downloads/DownloadState.cs` 定义的 13 个状态与 07-DownloadSubsystem.md §3.1 完全一致。

### 2.2 状态转换规则 ✅ 完全匹配

`Domain/Downloads/DownloadStateMachine.cs` 中的转换规则与 07-DownloadSubsystem.md §3.2 完全一致。

### 2.3 实现方式 — 合理偏离

| 文档定义 | 实际代码 |
|----------|----------|
| 内联 `Dictionary<DownloadState, HashSet<DownloadState>>` | 继承 `StateMachine<TState>` 基类 + `DefineTransition()` |
| 错误代码 `"DL_INVALID_TRANSITION"` | `"SM_INVALID_TRANSITION"`（通用状态机错误码）|

**评估**：✅ 功能等价且更好，基类 `StateMachine<T>` 可复用（`InstallStateMachine` 也使用）。

### 2.4 UI 状态映射 ✅ 完全匹配

13 个映射关系与文档完全一致（除 `Installing` 值不存在外，见 R3-07）。

---

## 三、错误处理合规性

### 3.1 Result 模型 ✅ 匹配 + 增强

**文件**：`Shared/Result.cs`

文档定义的所有成员均存在。代码额外增加：
- `bool IsFailure` — 便利属性
- `Result.Fail<T>(code, userMessage)` — 泛型快捷方法
- `Map<TOut>()` / `Bind<TOut>()` — 链式操作

### 3.2 Error 模型 ✅ 匹配

**文件**：`Shared/Error.cs`

6 个属性完全对应。代码使用 `required init`（比文档的 `init` 更严格）。

### 3.3 ErrorSeverity 枚举 ✅ 完全匹配

**文件**：`Shared/ErrorSeverity.cs`

`Warning, Error, Critical, Fatal` 全部匹配。

### 3.4 Infrastructure 错误处理模式 ✅ 合规

抽样检查多个 Infrastructure 服务（EpicOAuthHandler, EngineVersionApiClient, HashingService, DownloadOrchestrator）：
- 在系统边界 `catch` 异常
- 转换为 `Result.Fail()` + 结构化 `Error`
- 包含 `Code`, `UserMessage`, `TechnicalMessage`, `Severity` 字段
- 无 `MessageBox` 或裸 `throw`

---

## 四、启动流程合规性

### 发现 R3-08（🟡 中等）— Phase 0 缺少骨架屏/Splash

**文件**：`App/App.xaml.cs` L68-75

文档 10-StartupPipeline.md 要求 Phase 0：
> 创建 MainWindow → 显示窗口 + 启动画面（Splash）或骨架屏

实际代码：
```csharp
_mainWindow = new MainWindow();
// ... TrayIcon 初始化 ...
_mainWindow.Activate();
```

`MainWindow` 直接 `Activate()` 显示，无 `ShowSplash()` 调用。Shell 页面在 `MainWindow.LoadShellPage()` 中加载，但没有骨架屏过渡。

---

### 发现 R3-09（🟡 中等）— Phase 2 恢复步骤未独立（重复确认 R1-06）

文档要求独立的 Phase 2 包含：
1. `IAuthService.TryRestoreSessionAsync()`
2. `IDownloadOrchestrator.RecoverAsync()`
3. 已安装资产索引加载

实际合并到 `StartBackgroundServicesAsync()` 中由各 Worker 间接完成。

---

### 发现 R3-10（🟡 中等）— Phase 3 延迟初始化项不完整

文档要求 Phase 3 包含 6 项：
1. Fab 资产目录刷新 — ❌ 未在启动流程中显式触发
2. 缩略图预热 — ❌ 未在启动流程中显式触发
3. 引擎版本列表刷新 — ❌ 未在启动流程中显式触发
4. 自动更新检查 — ✅ AppUpdateWorker.Start()
5. 诊断信息收集 — ❌ 未在启动流程中显式触发
6. 清理临时文件 — ❌ 未在启动流程中显式触发

实际 Phase 3 仅启动 4 个 Worker（TokenRefresh, AutoInstall, AppUpdate, NetworkMonitor）。

---

## 五、日志策略合规性

### 5.1 Serilog 配置 ✅ 合规且超出

**文件**：`App/App.xaml.cs` L296-345

| 文档要求 | 代码状态 |
|----------|----------|
| Serilog.Sinks.File | ✅ 三路文件 Sink |
| Serilog.Sinks.Console | ✅ `#if DEBUG` |
| Serilog.Enrichers.Thread | ✅ |
| Serilog.Formatting.Compact | ✅ |
| FromLogContext | ✅ |
| AppVersion 属性 | ✅ |

**额外实现**：下载模块专用日志 Sink（`download-.log`）。

### 发现 R3-11（🟢 轻微）— 缺少 Serilog.Enrichers.Environment

文档 15-LoggingStrategy.md §2 列出 `Serilog.Enrichers.Environment → 机器名/进程名附加`。代码中 csproj 有此包引用但 Serilog 配置中未调用 `.Enrich.WithMachineName()`。

### 5.2 结构化日志模板 ✅ 完全合规

在 `src/` 目录中搜索字符串插值日志调用 `$"..."`：**零匹配**。所有日志调用使用结构化模板参数 `{TaskId}`。

### 5.3 OperationContext ✅ 合规

**文件**：`Shared/Logging/OperationContext.cs`

`CorrelationId`, `Module`, `Operation`, `StartedAt`, `PushToLogContext()` 全部匹配文档。`CompositeDisposable` 实现比文档示例更可靠。

### 5.4 额外日志工具

- `OperationTimer.cs` — using 模式自动记录操作耗时（文档未提及，实用增强）
- `LogSanitizer.cs` — Token/URL 脱敏（符合 L-04"绝不记录敏感信息"原则）

---

## 六、状态管理合规性

### 6.1 运行时业务状态 ⚠️ 存在但接口偏离

`IDownloadRuntimeStore` 存在并运作，但签名与 08-StateManagement.md 定义大幅偏离（详见 R3-06）。

### 6.2 持久化状态 ✅ 合规

- 下载任务/断点 → SQLite（`IDownloadTaskRepository`）
- 配置 → `appsettings.json` + `IAppConfigProvider`
- 安装记录 → SQLite（`IInstallationRepository`）

### 6.3 页面状态 ✅ 合规

所有 ViewModel 使用 `[ObservableProperty]` 私有字段存储页面状态。

### 发现 R3-12（🟢 轻微）— ShellState 未独立为类

文档 08-StateManagement.md 定义了独立的 `ShellState` 类，实际全局 UI 状态字段直接放在 `ShellViewModel` 中。功能等价但不符合文档分离设计。

---

## 审查总结

| 类别 | 通过 | 问题 |
|------|------|------|
| 核心接口签名 | 3/13 完全匹配 | 10 项有差异 |
| 下载状态机 | ✅ | 功能完全匹配 |
| 错误处理 | ✅ | 匹配 + 增强 |
| 启动流程 | ⚠️ | 3 🟡 |
| 日志策略 | ✅ | 1 🟢 |
| 状态管理 | ⚠️ | 接口偏离 + ShellState 未分离 |

### 发现汇总

| ID | 严重度 | 位置 | 摘要 |
|----|--------|------|------|
| R3-01 | 🟡 | IAuthService.cs | SessionExpired 事件未在核心接口文档中记录 |
| R3-02 | 🟡 | IDownloadCommandService.cs | PauseAllAsync/ResumeAllAsync 未文档化 |
| R3-03 | 🔴 | DownloadOrchestrator.cs | IDownloadOrchestrator **接口** 不存在，仅有具体类 |
| R3-04 | 🟡 | DownloadScheduler.cs, ChunkDownloadClient.cs | IDownloadScheduler/IChunkDownloader 接口不存在 |
| R3-05 | 🟡 | IDownloadTaskRepository.cs | IDownloadCheckpointRepository 被合并到 IDownloadTaskRepository |
| R3-06 | 🟡 | IDownloadRuntimeStore.cs | 接口签名与文档大幅偏离（方法名/事件类型/DTO名均不同） |
| R3-07 | 🟢 | DownloadState.cs | DownloadUiState 缺少 Installing 值 |
| R3-08 | 🟡 | App.xaml.cs | Phase 0 缺少骨架屏/Splash |
| R3-09 | 🟡 | App.xaml.cs | Phase 2 恢复步骤未独立（同 R1-06） |
| R3-10 | 🟡 | App.xaml.cs | Phase 3 延迟初始化仅 1/6 项完成 |
| R3-11 | 🟢 | App.xaml.cs Serilog | 缺少 Enrichers.Environment MachineName |
| R3-12 | 🟢 | ShellViewModel.cs | ShellState 未独立为类 |

**总计**：12 个问题（1 🔴 严重 + 8 🟡 中等 + 3 🟢 轻微）

### 累计发现（第1+2+3遍）

| 统计 | 第1遍 | 第2遍 | 第3遍 | 累计 |
|------|-------|-------|-------|------|
| 🔴 严重 | 1 | 1 | 1 | **3** |
| 🟡 中等 | 4 | 4 | 8 | **16** |
| 🟢 轻微 | 1 | 2 | 3 | **6** |
| **合计** | 6 | 7 | 12 | **25** |

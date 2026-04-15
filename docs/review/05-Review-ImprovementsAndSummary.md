# 第5遍审查：可改进项与最终总结

> 审查人：AI Agent  
> 日期：2026-04-16  
> 前序审查：01/02/03/04-Review-*.md  
> 审查重点：代码重复(DRY)、命名一致性、性能、测试覆盖度、硬编码值、可读性、安全性(OWASP)

---

## 一、代码重复（DRY）

### R5-01（🟡 中等）— Error 对象创建冗余

**文件**：`Infrastructure/Downloads/DownloadOrchestrator.cs` L93-99, L137-143, L173-179, L262-268

```csharp
return Result.Fail(new Error
{
    Code = "DL_NOT_FOUND",
    UserMessage = "下载任务不存在",
    TechnicalMessage = $"TaskId={taskId} not found",
    Severity = ErrorSeverity.Warning
});
```

`DL_NOT_FOUND` Error 在同一文件重复 4 次（`PauseAsync`/`ResumeAsync`/`CancelAsync`/`SetPriorityAsync`），且 `PluginCommandService`、`InstallCommandService`、`EngineVersionCommandService` 都有类似的 "not found" 错误重复。

全项目共计 20+ 处 `Result.Fail(new Error { ... })` 内联创建，相同 Code（如 `DL_NOT_FOUND`、`INSTALL_NOT_FOUND`、`PLUGIN_NOT_FOUND`）在同文件反复出现。

**改进建议**：在各模块中引入静态错误工厂（如 `DownloadErrors.NotFound(taskId)`），集中管理错误码和消息模板。

---

### R5-02（🟡 中等）— Polly ResiliencePipeline 配置重复

**文件**：
- `Infrastructure/FabLibrary/FabApiClient.cs` L36-55
- `Infrastructure/EngineVersions/EngineVersionApiClient.cs` L33-49

两个 API 客户端几乎完全重复了同一套 Polly 配置（3 次重试、指数退避、1秒延迟、处理 500+/HttpRequestException）。唯一差异是 FabApiClient 额外处理了 `TaskCanceledException` 并有 OnRetry 日志回调。

**改进建议**：提取公共工厂方法 `HttpResiliencePipelineFactory.CreateDefaultPipeline()` 或在 DI 中集中配置 `IHttpClientFactory` + Polly。

---

### R5-03（🟡 中等）— JsonSerializerOptions 重复定义

**文件**：
- `Infrastructure/FabLibrary/FabApiClient.cs` L28-31
- `Infrastructure/EngineVersions/EngineVersionApiClient.cs` L27-30
- `Infrastructure/Updates/AppUpdateService.cs` L48-52
- `Infrastructure/Settings/SettingsService.cs` L20-23

4 处独立定义了 `JsonSerializerOptions`，策略相同（`SnakeCaseLower` 或 `CamelCase`）。每次创建新的 `static readonly` 实例。

**改进建议**：在 `Shared` 层提供 `JsonDefaults.SnakeCaseLower` / `JsonDefaults.CamelCase` 集中管理。

---

### R5-04（🟢 轻微）— ViewModel Load 模式重复

**文件**：所有 ViewModel（6 个 ViewModel 文件）

每个 ViewModel 的 `LoadAsync` 方法都遵循几乎相同的模式：
```csharp
IsLoading = true;
try {
    // 加载逻辑
    // 更新 HasXxx 状态
}
finally {
    IsLoading = false;
}
```

这是 MVVM 的典型模式，不强制抽象，但如果引入 `LoadingGuard` 或基类方法可以减少样板代码。

---

## 二、命名一致性

### R5-05（🟡 中等）— 日志 Logger 实例命名不一致

**文件**：多个文件

三种日志声明风格并存：

| 风格 | 示例文件 |
|------|----------|
| `private readonly ILogger _logger = Log.ForContext<T>()` | DownloadOrchestrator.cs, DownloadReadService.cs |
| `private static readonly ILogger Logger = Log.ForContext<T>()` | EngineVersionApiClient.cs, SettingsService.cs, DownloadStateMachineTests.cs |
| `private readonly ILogger _logger;` (DI 注入) | 不存在 — 但 RepositoryBase 使用 `_logger` 实例字段 |

在同一项目中混用 `_logger`（实例字段）和 `Logger`（静态字段），以及 `readonly` vs `static readonly`。

**改进建议**：统一为一种风格。推荐 `private static readonly ILogger Logger = Log.ForContext<T>();`（静态、大写开头），因为 Serilog ForContext 不依赖实例状态。

---

### R5-06（🟢 轻微）— 中英文混用的日志消息

**文件**：全项目

日志消息在中文和英文之间交替：
- `_logger.Information("开始下载: {AssetName}")` — 中文
- `_logger.Debug("查询 {Table} | Id={Id} | 找到={Found}")` — 中文
- `_logger.Warning("Fab API 重试 #{Attempt}")` — 中文+英文

**影响**：在国际化团队中搜索日志关键词不便。
**改进建议**：如果目标用户是中文团队，保持中文即可；否则统一为英文。这是风格选择，不是 Bug。

---

### R5-07（🟢 轻微）— 同一概念使用不同方法名

**文件**：多个 ViewModel

- `FabLibraryViewModel.LoadAsync()` vs `DownloadsViewModel.LoadAsync()` — 一致 ✅
- `FabLibraryViewModel.RefreshAsync()` vs `EngineVersionsViewModel` 无 Refresh — 不一致
- `DownloadsViewModel.PauseAsync(taskId)` vs `DownloadCommandService.PauseAsync(taskId, ct)` — ViewModel 层总忽略 CT

---

## 三、性能

### R5-08（🟡 中等）— SettingsService.Clone 深拷贝使用 JSON 序列化

**文件**：`Infrastructure/Settings/SettingsService.cs` L218-222

```csharp
private static T Clone<T>(T source) where T : class
{
    string json = JsonSerializer.Serialize(source, JsonOptions);
    return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
}
```

每次读取/写入配置都经过 JSON 序列化 + 反序列化做深拷贝（`GetDownloadConfig()`、`GetAppearanceConfig()` 等）。对于简单 POCO 对象，这是昂贵的操作。

**改进建议**：改用 `record` 类型（天然不可变）或手动浅拷贝。

---

### R5-09（🟡 中等）— GetActiveTaskIdsAsync 和 GetPausedTaskIdsAsync 全量加载后过滤

**文件**：`Infrastructure/Downloads/DownloadOrchestrator.cs` L235-256

```csharp
var tasks = await _repository.GetActiveTasksAsync(ct);
return tasks.Where(t => t.State is not (...)).Select(t => t.Id).ToList();
```

两个方法都调用 `GetActiveTasksAsync()` 从数据库加载全部活跃任务实体，然后在内存中按状态过滤。

**改进建议**：在 Repository 层添加 `GetTaskIdsByStateAsync(params DownloadState[] states)` 方法，直接 SQL 过滤。

---

### R5-10（🟢 轻微）— ObservableCollection 逐条添加无批量操作

**文件**：所有 ViewModel

```csharp
foreach (var v in availableResult.Value!)
    AvailableVersions.Add(new EngineVersionItemViewModel(v));
```

每次 `Add` 触发一次 `CollectionChanged` 事件和 UI 刷新。100+ 项时会导致可感知的卡顿。

**改进建议**：使用 `AddRange` 扩展或先 `Clear()` 后批量赋值新集合。

---

### R5-11（🟢 轻微）— ConfigureAwait(false) 缺失

**文件**：全部 Infrastructure 和 Background 层（除 `App.xaml.cs` L91 外无任何使用）

整个项目仅有 1 处 `ConfigureAwait(false)`。非 UI 层（Infrastructure、Background）的所有异步方法均未添加。

**影响**：在 WinUI 3 中默认无 SynchronizationContext 问题不大，但如果代码被移植或测试框架注入了 SyncContext，可能引发死锁。属于防御性编程。

---

## 四、测试覆盖度

### R5-12（🔴 严重）— 核心子系统测试覆盖不足

**现有测试（13 个测试类）**：

| 测试类 | 覆盖对象 | 层级 |
|--------|----------|------|
| SanityTests × 2 | 框架验证 | - |
| DownloadStateMachineTests | Domain.Downloads.DownloadStateMachine | Domain |
| DownloadTaskTests | Domain.Downloads.DownloadTask | Domain |
| InstallStateMachineTests | Domain.Installations.InstallStateMachine | Domain |
| ResultTests | Shared.Result | Shared |
| DownloadRuntimeStoreTests | Infrastructure.Downloads.DownloadRuntimeStore | Infrastructure |
| HashingServiceTests | Infrastructure.Installations.HashingService | Infrastructure |
| IntegrityVerifierTests | Infrastructure.Installations.IntegrityVerifier | Infrastructure |
| InstallationTests | Infrastructure.Installations.Installation | Infrastructure |
| FabApiClientTests | Infrastructure.FabLibrary.FabApiClient | Infrastructure |
| AutoInstallWorkerTests | Background.Installations.AutoInstallWorker | Background |
| NavigationServiceTests | Presentation.Shell.NavigationService | Presentation |
| RepairAsyncTests | Infrastructure.Installations.RepairFileDownloader | Infrastructure |
| RepositoryBaseTests | Infrastructure.Persistence.RepositoryBase | Infrastructure (集成) |

**无测试覆盖的关键模块**：

| 缺失测试 | 风险等级 | 理由 |
|-----------|---------|------|
| **DownloadOrchestrator** | 🔴 高 | 下载核心编排逻辑（入队/暂停/恢复/取消/崩溃恢复），包含 R4-04 状态转换 Bug |
| **DownloadScheduler** | 🔴 高 | 调度核心（并发控制），包含 R4-01 火后不管 Bug |
| **ChunkDownloadClient** | 🔴 高 | 分片下载（Polly 重试），包含 R4-02 误重试 Bug |
| **DownloadCommandService** | 中等 | 命令入口（PauseAll/ResumeAll 逻辑） |
| **AuthService** | 🔴 高 | 认证核心（登录/登出/Token 刷新），包含 R4-09 竞态 Bug |
| **EpicOAuthHandler** | 高 | OAuth 流程（HTTP 监听/Token 交换） |
| **SettingsService** | 中等 | 配置读写（包含文件 I/O） |
| **AppUpdateService** | 中等 | 自动更新（包含 GitHub API 交互） |
| **PluginCommandService** | 中等 | 插件管理操作 |
| **EngineVersionCommandService** | 中等 | 引擎版本安装/卸载 |
| **所有 ViewModel** | 中等 | 无任何 ViewModel 单元测试 |
| **MigrationRunner** | 低 | 数据库迁移 |
| **ThemeService** | 低 | 主题切换 |

**覆盖度估算**：

- Domain 层：~70%（两个状态机 + DownloadTask 有测试，但 Entity/ValueObject 基类无测试）
- Infrastructure 层：~25%（仅 DownloadRuntimeStore/HashingService/IntegrityVerifier/FabApiClient/InstallationRepo/RepairFileDownloader 有测试）
- Background 层：~33%（仅 AutoInstallWorker 有测试）
- Presentation 层：~5%（仅 NavigationService 有测试）
- Shared 层：~30%（仅 Result 有测试）
- App 层：0%

**改进建议**：优先为 `DownloadOrchestrator`、`DownloadScheduler`、`AuthService` 编写单元测试（这三个包含已发现的 🔴 级别 Bug）。

---

## 五、硬编码值

### R5-13（🟡 中等）— 应用名字符串 "HelsincyEpicLauncher" 散布全项目

**文件**：8 处独立硬编码（详见 grep 结果）

- `TrayIconManager.cs` L30: `Text = "HelsincyEpicLauncher"`
- `MainWindow.xaml.cs` L22: `Title = "HelsincyEpicLauncher"`
- `AppUpdateService.cs` L145, L334, L336: 临时目录、EventLog Source
- `AppConfigProvider.cs` L20: LocalAppData 路径
- `DependencyInjection.cs` L123: User-Agent
- `InstallationRepository.cs` L119: manifest 路径

**改进建议**：抽取为 `AppConstants.AppName` 常量或从 `IAppConfigProvider` 获取。

---

### R5-14（🟡 中等）— 魔法数字散布

**文件**：多处

| 位置 | 值 | 含义 |
|------|---|------|
| `DownloadOrchestrator.cs` L39 | `1.2` | 磁盘空间检查系数（需要 120%） |
| `DownloadRuntimeStore.cs` L131 | `5` 秒 | 速度计算采样窗口 |
| `FabLibraryViewModel.cs` L56 | `20` | 分页大小 |
| `FabLibraryViewModel.cs` L57 | `300` ms | 搜索防抖时间 |
| `DownloadsViewModel.cs` L76 | `50` | 历史记录查询限制 |
| `NotificationService.cs` L17-19 | `4/6/8` 秒 | 通知显示时长 |
| `ChunkDownloadClient.cs` L200 | `30` 秒 | 断路器采样/中断时长 |

这些值分散在各处且没有明确的配置归属。

**改进建议**：将可调节参数提取到 `IAppConfigProvider` 或 `UserSettings`，将固定策略参数定义为常量并添加说明注释。

---

### R5-15（🟢 轻微）— API URL 硬编码在 DI 注册中

**文件**：`Infrastructure/DependencyInjection.cs` L93, L113, L120

```csharp
client.BaseAddress = new Uri("https://www.fab.com/api");
client.BaseAddress = new Uri("https://www.unrealengine.com/api");
client.BaseAddress = new Uri("https://api.github.com");
```

以及 `EpicOAuthHandler.cs` 中 4 个 API URL 常量和 `AppUpdateService.cs` L34 的 GitHub API URL。

**改进建议**：移至 `appsettings.json` 配置文件。

---

## 六、可读性

### R5-16（🟢 轻微）— DownloadOrchestrator.RecoverAsync 过长 + 嵌套逻辑

**文件**：`Infrastructure/Downloads/DownloadOrchestrator.cs` L206-230

恢复逻辑中 `if → if → if` 三层嵌套，且 Failed → Queued 的意图不直观（已在 R4-04 标记为 Bug）。拆分为 `RecoverSingleTaskAsync` 方法可提高可读性。

---

### R5-17（🟢 轻微）— SettingsService 4 个 Update 方法完全同构

**文件**：`Infrastructure/Settings/SettingsService.cs` L78-94

```csharp
public Task<Result> UpdateDownloadConfigAsync(DownloadConfig config, CancellationToken ct)
    => UpdateSectionAsync("Download", s => s.Download = Clone(config));
public Task<Result> UpdateAppearanceConfigAsync(AppearanceConfig config, CancellationToken ct)
    => UpdateSectionAsync("Appearance", s => s.Appearance = Clone(config));
// ... 重复两个
```

4 个委托方法结构完全相同，仅 section 名和属性不同。当前已足够简洁，但若未来新增 section 需要扩展时考虑泛化。

---

## 七、安全性（OWASP）

### R5-18（🟡 中等）— 错误信息可能泄漏内部实现细节

**文件**：多处 Error 对象

```csharp
TechnicalMessage = ex.Message,
```

`TechnicalMessage` 包含完整的异常消息（堆栈、SQL 错误等），虽然文档声明"仅日志/诊断用"，但如果前端部分意外展示给用户（如 debug toast），可能泄漏系统内部信息。

**现状**：当前 ViewModel 层仅使用 `Error.UserMessage`，暂无泄漏路径。标记为提醒。

---

### R5-19（🟡 中等）— HTTP 通信未强制 HTTPS 验证

**文件**：`Infrastructure/DependencyInjection.cs` — HttpClient 注册

所有 HttpClient 使用默认 `HttpClientHandler`，未显式禁用不安全的 TLS 版本或配置证书验证策略。当前所有 URL 均为 HTTPS，但未在代码层面强制（如检查 `BaseAddress.Scheme == "https"`）。

**影响**：如果配置被修改为 HTTP，将以明文传输 OAuth token。

---

### R5-20（🟡 中等）— LogSanitizer 未在 AuthService 日志中使用

**文件**：`Shared/Logging/LogSanitizer.cs` 已实现、`Infrastructure/Auth/AuthService.cs`

`LogSanitizer.MaskToken()` 和 `SanitizeUrl()` 已定义但未在 `AuthService` 的日志调用中使用。AuthService 当前未记录 token 值，但也未使用 `MaskToken` 做防护。

**改进建议**：在涉及 token 的日志点显式使用 `LogSanitizer`，防止未来维护引入泄漏。

---

## 审查总结

| 类别 | 🔴 | 🟡 | 🟢 |
|------|-----|-----|-----|
| 代码重复 (DRY) | 0 | 3 | 1 |
| 命名一致性 | 0 | 1 | 2 |
| 性能 | 0 | 2 | 2 |
| 测试覆盖度 | 1 | 0 | 0 |
| 硬编码值 | 0 | 2 | 1 |
| 可读性 | 0 | 0 | 2 |
| 安全性 | 0 | 3 | 0 |

### 发现汇总

| ID | 严重度 | 类别 | 摘要 |
|----|--------|------|------|
| R5-01 | 🟡 | DRY | Error 对象创建冗余（DL_NOT_FOUND × 4 等） |
| R5-02 | 🟡 | DRY | Polly Pipeline 配置重复（FabApi/EngineVersionApi） |
| R5-03 | 🟡 | DRY | JsonSerializerOptions × 4 重复定义 |
| R5-04 | 🟢 | DRY | ViewModel Load 模式样板代码 |
| R5-05 | 🟡 | 命名 | Logger 实例命名不一致（_logger vs Logger, static vs instance） |
| R5-06 | 🟢 | 命名 | 日志消息中英文混用 |
| R5-07 | 🟢 | 命名 | ViewModel 方法名跨页面不一致 |
| R5-08 | 🟡 | 性能 | SettingsService.Clone 使用 JSON 序列化做深拷贝 |
| R5-09 | 🟡 | 性能 | GetActiveTaskIdsAsync 全量加载后内存过滤 |
| R5-10 | 🟢 | 性能 | ObservableCollection 逐条 Add 触发 N 次 UI 刷新 |
| R5-11 | 🟢 | 性能 | ConfigureAwait(false) 全项目几乎未使用 |
| R5-12 | 🔴 | 测试 | 核心子系统无测试（Orchestrator/Scheduler/AuthService 等） |
| R5-13 | 🟡 | 硬编码 | "HelsincyEpicLauncher" 字符串 8 处散布 |
| R5-14 | 🟡 | 硬编码 | 魔法数字散布（页大小/超时/系数等未集中管理） |
| R5-15 | 🟢 | 硬编码 | API URL 硬编码在 DI 和 const 中 |
| R5-16 | 🟢 | 可读性 | RecoverAsync 过长嵌套 |
| R5-17 | 🟢 | 可读性 | SettingsService 4 个同构 Update 方法 |
| R5-18 | 🟡 | 安全 | TechnicalMessage 含完整异常信息（潜在泄漏） |
| R5-19 | 🟡 | 安全 | HTTP 通信未强制 HTTPS 验证 |
| R5-20 | 🟡 | 安全 | LogSanitizer 已实现但未在 Auth 日志中使用 |

**本轮**：20 个问题（1 🔴 + 11 🟡 + 8 🟢）

---

## 五轮审查最终总结

### 累计发现统计

| 轮次 | 焦点 | 🔴 | 🟡 | 🟢 | 合计 |
|------|------|-----|-----|-----|------|
| 第1遍 | 架构合规 | 1 | 4 | 1 | 6 |
| 第2遍 | 模块耦合 | 1 | 4 | 2 | 7 |
| 第3遍 | 契约合规 | 1 | 8 | 3 | 12 |
| 第4遍 | Bug与边界 | 3 | 19 | 4 | 26 |
| 第5遍 | 改进建议 | 1 | 11 | 8 | 20 |
| **总计** | | **7** | **46** | **18** | **71** |

### Top 10 最高优先级修复项

| 优先级 | ID | 描述 | 原因 |
|--------|----|------|------|
| P1 | R4-08 | ShellViewModel.OnSessionExpired 非 UI 线程崩溃 | 确定性崩溃，后台线程触发 |
| P2 | R4-13 | App.xaml.cs GetAwaiter().GetResult() 死锁风险 | 脆弱的同步等待，一个 ConfigureAwait 遗漏即死锁 |
| P3 | R4-01 | DownloadScheduler 异常静默丢失 | 调度器可能静默停止工作 |
| P4 | R4-09 | AuthService TOCTOU 竞态 | 登出后 token 可能被写回 |
| P5 | R5-12 | 核心子系统无单元测试 | 多个 🔴 Bug 本应被测试发现 |
| P6 | R1-05 | ThemeService Presentation 层直接文件 I/O | 架构违规 |
| P7 | R2-01 | RepairDownloadUrlProvider 直接引用具体类 | P-01 禁止项违规 |
| P8 | R3-03 | IDownloadOrchestrator 接口不存在 | DIP 违规 |
| P9 | R4-02 | Polly 误重试用户取消 | 暂停响应延迟 31 秒 |
| P10 | R4-16 | InstallationRepository 路径遍历风险 | OWASP 安全风险 |

### 代码质量综合评分

| 维度 | 评分 (1-10) | 说明 |
|------|-------------|------|
| 架构设计 | **8.0** | 7 层架构清晰，DI 注册规范，文档完善。个别跨层违规（ThemeService I/O、RepairDownloadUrlProvider 耦合） |
| 代码规范 | **7.5** | CommunityToolkit.Mvvm + Result 模式统一，命名基本一致。Logger/Error/JSON 配置存在不一致 |
| 安全性 | **6.5** | OAuth 密钥硬编码、路径遍历、TechnicalMessage 泄漏风险。LogSanitizer 已实现但未充分使用 |
| 可靠性 | **6.0** | 3 个严重 Bug（线程崩溃/死锁/调度停止），多个竞态条件和资源泄漏 |
| 测试覆盖 | **4.5** | 仅覆盖 Domain + 部分 Infrastructure，核心编排/Auth/调度器/ViewModel 全无测试 |
| 性能 | **7.0** | 整体合理。JSON 深拷贝设置、全量加载过滤、逐条 ObservableCollection Add 可优化 |
| 可维护性 | **7.5** | 模块化好，文档齐全，Error 模型统一。但 Error 工厂缺失、Magic Number 分散增加维护成本 |

**综合评分：6.7 / 10**

### 评价

该项目架构设计扎实，文档与代码结构高度对齐，采用了 Result 模式、状态机、DI 等现代 .NET 最佳实践。主要风险集中在：
1. **可靠性**：线程安全和异步编程存在数个确定性 Bug
2. **测试覆盖**：核心子系统缺乏测试，Bug 在正常使用中才会暴露
3. **安全**：OAuth 凭据管理和路径验证需要加固

建议按 Top 10 优先级逐步修复，优先解决 P1-P4（确定性 Bug），然后补充测试覆盖（P5），最后处理架构和代码质量改进。

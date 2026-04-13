# 会话交接文档

## 最后更新
- 时间：2026-04-13
- 完成任务：Task 5.3（Uninstaller + Installations UI）

## 当前项目状态
- 最后成功编译：是（dotnet build 9 个项目零错误零警告）
- 最后测试结果：全部通过（158/158）
- 当前 Phase：Phase 5 完成（Task 5.1 + 5.2 + 5.3 全部完成）
- 下一个任务：Task 6.1（Fab API 客户端）

## 本次会话完成的工作

### Task 4.1 — DownloadTask 领域实体 + 状态机
- DownloadState（13 状态）、DownloadStateMachine（17 转换）、DownloadTask 实体、ChunkInfo/DownloadCheckpoint 值对象
- 46 个单元测试

### Task 4.2 — Download Orchestrator + Scheduler + 服务层
- DownloadScheduler（并发+优先级）、DownloadOrchestrator（全流程编排）、Command/Read Service、Repository

### Task 4.3 — ChunkDownloader + HTTP Range + Polly 韧性
- ChunkDownloadClient、Polly ResiliencePipeline（重试+超时+断路器）、分块策略、原子写入

### Task 4.4 — Checkpoint 持久化 + 崩溃恢复
- Migration_005_DownloadCheckpoints、检查点 CRUD、DownloadOrchestrator 崩溃恢复逻辑

### Task 4.5 — DownloadRuntimeStore + 进度聚合
- ConcurrentDictionary 快照管理、SpeedCalculator（5s 滑动窗口、500ms 节流）、ETA 计算

### Task 4.6 — Downloads UI 页面
- IDownloadRuntimeStore 应用层接口、DownloadsViewModel/Page、ShellViewModel 下载速度状态栏

### Task 5.1 — Install Worker + Manifest
- InstallState（8 状态）、InstallStateMachine（16 转换）、InstallManifest/ManifestFileEntry
- Installation 实体、全套契约、InstallationRepository（SQLite+Manifest JSON）、InstallWorker（ZIP+Zip Slip 防护）
- InstallCommandService/InstallReadService

### Task 5.2 — Integrity Verifier + Repair
- IHashingService/HashingService（SHA-256、并行多文件）
- IIntegrityVerifier/IntegrityVerifier（两遍扫描：缺失+哈希校验）
- InstallCommandService.RepairAsync 完整实现（Manifest 加载→校验→报告→状态转换）

### Task 5.3 — Uninstaller + Installations UI
- InstallationsViewModel：Load/Verify/Repair/Uninstall 命令、InstallItemViewModel 列表项
- InstallationsPage.xaml：资产卡片列表（名称/版本/大小/路径/安装时间）、校验/修复/卸载按钮、空状态
- NavigationRoute + NavigationService + ShellPage 添加"已安装"导航项
- ShellViewModel 添加 NavigateToInstallations 命令
- Presentation DI 注册 InstallationsViewModel

## 遗留问题
- RepairAsync 目前仅检测损坏文件并记录日志，实际重新下载损坏文件需要 Downloads 模块配合（后续任务）
- "下载完自动安装"开关功能需要 DownloadCompletedEvent → InstallAsync 联动，留待 Phase 6 或 7 整合

## 下一个任务的输入
- 读取文档：docs/06-ModuleDefinitions/FabLibrary.md
- Task 6.1：Fab API 客户端
  - IFabCatalogReadService / IFabAssetCommandService 实现
  - Fab API HTTP 客户端（搜索、详情、已拥有查询）
  - API DTO 映射
  - Polly 韧性策略（重试 + 缓存）
  - 搜索结果本地缓存（5 分钟过期）

## 关键约束提醒
- 文件名英文，内容中文（代码除外，注释中文）
- 仅在 Q:\MyEpicLauncher 目录内操作
- 项目名称为 HelsincyEpicLauncher，解决方案文件 HelsincyEpicLauncher.slnx
- 每个代码文件顶部必须有版权声明：// Copyright (c) Helsincy. All rights reserved.
- 不含游戏商店、游戏库存模块；包含 Fab 资产库模块
- Result API: Result.Ok() / Result.Fail(Error) / Result.Ok<T>(value) / Result.Fail<T>(Error)
- Error 使用 required init 属性：new Error { Code, UserMessage, TechnicalMessage, CanRetry, Severity }
- Serilog: Log.ForContext<T>() 模式，不用 ILogger
- Entity<TId>: 无构造参数，Id 用 protected setter 赋值
- ViewModelLocator.Resolve<T>() 模式
- PowerShell Set-Content 会破坏中文编码，禁止使用
- **每个原子任务完成后必须同步更新 CHANGELOG.md 和 SESSION_HANDOFF.md**

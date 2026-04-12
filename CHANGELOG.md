# Changelog

## [Unreleased]

### Task 0.2 - DI 容器 + 配置系统 (2026-04-12)
- 创建 IAppConfigProvider 接口（Shared 层 — AppVersion / 各路径 / 下载参数）
- 创建 AppConfigProvider 实现（Infrastructure 层 — 读取 IConfiguration，默认 %LOCALAPPDATA%）
- 更新 appsettings.json 添加 Paths 和 Downloads 配置节
- 实现 Program.cs 完整 DI 容器构建流程（ConfigurationBuilder → ServiceCollection → BuildServiceProvider）
- 实现各层 AddXxx() 扩展方法（Domain / Application / Infrastructure / Presentation / Background）
- 为各层项目添加 Microsoft.Extensions.DependencyInjection.Abstractions 包引用
- 为 Infrastructure 添加 Microsoft.Extensions.Configuration.Abstractions 包引用
- dotnet build 9 个项目零错误，dotnet test 2/2 通过

### Task 0.1 - 创建 Solution 和项目文件 (2026-04-12)
- 创建 HelsincyEpicLauncher.slnx 解决方案
- 创建 7 个源码项目：App / Presentation / Application / Domain / Infrastructure / Background / Shared
- 创建 2 个测试项目：Tests.Unit / Tests.Integration
- 配置项目引用关系（按架构依赖图）
- 引入全部 NuGet 包（WindowsAppSDK, CommunityToolkit.Mvvm, Serilog, SQLite, Polly, xUnit 等）
- 创建 Directory.Build.props（统一 TFM/版本/版权 + dotnet CLI 的 AppxPackage 路径修复）
- 创建 global.json（固定 .NET 9.0.309 SDK）
- 创建 .editorconfig 代码风格规范
- 创建 app.manifest（DPI 感知）
- 各层 DependencyInjection.cs 占位
- 2 个 Sanity 测试通过
- dotnet build 零错误零警告
- 技术变更：从 .NET 8 改为 .NET 9（系统无 .NET 8 SDK）

### 文档阶段 (2024-12-15)
- 创建 20+ 架构设计文档（docs/ 目录）
- 覆盖：项目总览、架构原则、解决方案结构、模块依赖、核心接口、10 个模块定义、下载子系统、状态管理、错误处理、启动流程、技术栈、AI 协作、开发阶段、反模式
- 新增日志策略文档（15-LoggingStrategy.md）
- 新增 AI 会话交接协议（12-AICollaboration.md § 8~11）
- 将开发计划细化为 41 个原子任务（13-DevelopmentPhases.md）

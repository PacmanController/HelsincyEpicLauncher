# 遗留问题修复 — 实现记录

> 日期：2026-04-15  
> 状态：已完成（169/169 测试通过）

## 修复内容

| # | 问题 | 解决方案 |
|---|------|---------|
| 1 | RepairAsync 只记日志不修复 | 依赖倒置获取新鲜 CDN URL → 下载整包 → 局部解压替换 → 二次校验 |
| 2 | AutoInstall 开关无后端联动 | AutoInstallWorker 事件驱动：DownloadCompleted → 检查开关 → InstallAsync |
| 3 | FabApiClient 无单元测试 | MockHttpMessageHandler + NSubstitute，8 个测试场景 |

## 架构决策

### 问题 1：修复时如何获取下载 URL？

**约束**：Installations → FabLibrary.Contracts 会造成循环依赖（FabLibrary 已依赖 Installations.Contracts）。

**解决**：依赖倒置
- 接口 `IRepairDownloadUrlProvider` 定义在 `Installations.Contracts`（Application 层）
- 实现 `RepairDownloadUrlProvider` 在 Infrastructure 层（可访问 `FabApiClient`）
- DI 注册连接两端

```
InstallCommandService → IRepairDownloadUrlProvider (Application 接口)
                              ↑ 实现
                       RepairDownloadUrlProvider → FabApiClient (Infrastructure)
```

### 问题 2：AutoInstall 通信路径

```
DownloadRuntimeStore.DownloadCompleted (Downloads 事件)
    → AutoInstallWorker (Background 层)
        → ISettingsReadService.GetDownloadConfig().AutoInstall
        → IInstallCommandService.InstallAsync (Installations 命令)
```

所有依赖均为 Contracts 接口，零模块内部耦合。

## 新增文件

| 文件 | 层级 | 说明 |
|------|------|------|
| `Application/Modules/Installations/Contracts/IRepairDownloadUrlProvider.cs` | Application | 修复 URL 获取接口 + RepairDownloadInfo DTO |
| `Infrastructure/Installations/RepairDownloadUrlProvider.cs` | Infrastructure | 接口实现，调用 FabApiClient |
| `Infrastructure/Installations/RepairFileDownloader.cs` | Infrastructure | 下载+解压+校验+替换辅助类 |
| `Background/Installations/AutoInstallWorker.cs` | Background | 自动安装事件监听 Worker |
| `Tests.Unit/RepairAsyncTests.cs` | Tests | RepairAsync 6 个测试 |
| `Tests.Unit/AutoInstallWorkerTests.cs` | Tests | AutoInstallWorker 3 个测试 |
| `Tests.Unit/FabApiClientTests.cs` | Tests | FabApiClient 8 个测试 |
| `Tests.Unit/Helpers/MockHttpMessageHandler.cs` | Tests | HTTP Mock 辅助类 |

## 修改文件

| 文件 | 变更 |
|------|------|
| `Domain/Installations/InstallManifest.cs` | +DownloadUrl? 字段 |
| `Domain/Installations/InstallStateMachine.cs` | +Installed→Repairing, +Repairing→NeedsRepair |
| `Infrastructure/Installations/InstallCommandService.cs` | 构造函数+2依赖，RepairAsync 完整重写 |
| `Infrastructure/Installations/InstallWorker.cs` | ExecuteAsync +downloadUrl 参数 |
| `Infrastructure/DependencyInjection.cs` | +IRepairDownloadUrlProvider, +RepairFileDownloader |
| `Infrastructure/Launcher.Infrastructure.csproj` | +InternalsVisibleTo Tests.Unit |
| `Background/DependencyInjection.cs` | +AutoInstallWorker |
| `App/App.xaml.cs` | +AutoInstallWorker.Start() |
| `Tests.Unit/Launcher.Tests.Unit.csproj` | +Background 项目引用 |
| `Tests.Unit/InstallStateMachineTests.cs` | 更新转换测试用例 |
| `Application/Modules/Installations/Contracts/InstallModels.cs` | +RepairFileResult DTO |

## RepairAsync 完整流程

```
1. 查找安装记录 → 状态转换 Installed→Repairing
2. 读取 InstallManifest
3. 完整性校验 (IIntegrityVerifier)
4. 如果校验通过 → Installed, 返回成功
5. 获取新鲜下载 URL (IRepairDownloadUrlProvider → FabApiClient)
6. 下载完整资产包到临时目录 (RepairFileDownloader)
7. ZIP: 仅解压损坏文件 + SHA-256 校验 + 原子替换
   单文件: 直接复制替换
8. 二次校验 → 全部通过: Installed | 仍有损坏: NeedsRepair
9. 发布 RepairCompletedEvent
10. 清理临时文件
```

## 测试覆盖

### RepairAsyncTests (6)
- NotFound → INSTALL_NOT_FOUND
- VerificationPassed → OK (无需修复)
- NoManifest → REPAIR_NO_MANIFEST, Failed
- UrlProviderFails → REPAIR_URL_FAILED, NeedsRepair
- VerificationFails → VERIFY_IO_ERROR, Failed
- InvalidStateTransition → SM_INVALID_TRANSITION

### AutoInstallWorkerTests (3)
- AutoInstall=true → 调用 InstallAsync
- AutoInstall=false → 不调用 InstallAsync
- InstallAsync 失败 → 不抛异常

### FabApiClientTests (8)
- SearchAsync 200 → OK + 正确反序列化
- SearchAsync 401 → FAB_HTTP_401
- SearchAsync 500 → 重试 + FAB_HTTP_500
- GetDetailAsync 200 → OK
- GetDownloadInfoAsync 200 → OK
- GetOwnedAssetsAsync 200 → OK
- Token 失败 → FAB_AUTH_FAILED
- 请求取消 → FAB_CANCELLED

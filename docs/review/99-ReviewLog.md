# 审查日志

> 实时记录每轮审查的执行状态、发现数量和关键摘要。

---

## 日志条目

| 时间 | 轮次 | 状态 | 发现数 | 摘要 |
|------|------|------|--------|------|
| 2026-04-16 | 准备 | ✅ 完成 | - | 审查计划编写完成，文件夹建立 |
| 2026-04-16 | 第1遍 | ✅ 完成 | 6 | 架构与分层合规性审查：1🔴 4🟡 1🟢 |
| 2026-04-16 | 第2遍 | ✅ 完成 | 7 | 模块耦合与依赖审查：1🔴 4🟡 2🟢 |

---

## 详细日志

### 2026-04-16 — 审查准备

- 创建 `docs/review/` 目录
- 编写审查计划 `00-ReviewPlan.md`
- 创建审查日志 `99-ReviewLog.md`
- 阅读全部设计文档（01~15 + 06-ModuleDefinitions 全部 10 个模块）
- 基线知识就绪，准备开始第 1 遍审查

### 2026-04-16 — 第1遍审查

- 审查范围：层级引用(csproj)、Code-Behind合规、ViewModel合规、Shell服务、DI注册、启动管线、Background隔离、Domain纯净
- 检查了全部 9 个 csproj 文件的 ProjectReference
- 逐文件审查所有 .xaml.cs (10个) 和 ViewModel (9个)  
- 关键发现：ThemeService.cs 在 Presentation 层直接执行文件 I/O（🔴）
- 关键发现：ShellViewModel 使用 Visibility 类型耦合 WinUI（🟡）
- 关键发现：2 个 ViewModel 硬编码安装路径（🟡）
- 输出文档：`01-Review-ArchitectureCompliance.md`

### 2026-04-16 — 第2遍审查

- 审查范围：P-01~P-06 禁止项、模块依赖表合规、反模式 AP-01~AP-06
- 逐文件检查 Infrastructure 层和 Background 层的 using 语句
- 对照 04-ModuleDependencyRules.md 依赖表逐模块校验
- 关键发现：RepairDownloadUrlProvider 直接引用 FabApiClient（🔴 P-01 违规）
- 关键发现：3 个模块依赖超出总依赖表（Auth.Contracts 未列入、Plugins→EngineVersions）
- 关键发现：IFabAssetCommandService 返回值泄漏 Downloads 域类型
- 反模式检查全部通过（ViewModel 最大 342 行、Service 最大 8 方法）
- 输出文档：`02-Review-ModuleCoupling.md`

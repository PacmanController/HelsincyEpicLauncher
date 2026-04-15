# 代码审查计划

> 项目：HelsincyEpicLauncher  
> 审查范围：Phase 0–8 全部已实现代码  
> 审查基线文档：docs/01~15 全部架构设计文档 + docs/06-ModuleDefinitions/*.md  
> 开始时间：2026-04-16  
> 审查总轮次：5

---

## 审查目标

1. **发现脱离文档的实现** — 代码是否严格按照文档约定实现
2. **发现强耦合** — 模块依赖是否违反 04-ModuleDependencyRules.md 的禁止项
3. **发现Bug** — 逻辑错误、边界条件遗漏、资源泄漏、线程安全问题
4. **发现可改进项** — 代码质量、性能、可维护性、一致性

---

## 审查轮次计划

### 第1遍：架构与分层合规性审查

**焦点**：验证六层架构（App/Presentation/Application/Domain/Infrastructure/Background/Shared）边界是否清晰

**检查项**：
- [ ] 各 .csproj 的 ProjectReference 是否严格遵守文档 03-SolutionStructure.md 的引用关系
- [ ] Presentation 层是否只依赖 Application/Domain(仅Contracts/DTO/枚举)/Shared
- [ ] Application 层是否只依赖 Domain/Shared
- [ ] Domain 层是否零外部依赖（仅 Shared）
- [ ] Infrastructure 层是否只依赖 Domain/Shared/各模块Contracts
- [ ] Background 层是否只依赖 Application/Domain/Shared/各模块Contracts（不直接依赖 Infrastructure）
- [ ] App 层是否只做 DI 注册和启动编排
- [ ] Page code-behind 是否只处理纯视觉逻辑（原则2确认）
- [ ] ViewModel 是否无业务逻辑（无 HTTP/SQL/IO 直接操作）

**输出文档**：`01-Review-ArchitectureCompliance.md`

---

### 第2遍：模块耦合与依赖审查

**焦点**：验证模块间通信方式是否符合 04-ModuleDependencyRules.md

**检查项**：
- [ ] 跨模块引用是否只通过 Contracts（禁止 P-01）
- [ ] 是否存在跨模块直接操作 ViewModel（禁止 P-02）
- [ ] 是否存在跨模块共享可变领域对象（禁止 P-03）
- [ ] 是否存在模块绕过 Contracts 直连数据库（禁止 P-04）
- [ ] 是否存在反向依赖（禁止 P-05）
- [ ] 是否存在循环依赖（禁止 P-06）
- [ ] DI 注册是否在 App 层集中完成
- [ ] 反模式 AP-01~AP-12 是否存在

**输出文档**：`02-Review-ModuleCoupling.md`

---

### 第3遍：文档契约 vs 实际实现审查

**焦点**：逐模块对照文档定义检查实际代码

**检查项**：
- [ ] 每个模块的接口签名是否与 05-CoreInterfaces.md 一致
- [ ] 下载状态机实现是否与 07-DownloadSubsystem.md 一致
- [ ] 错误处理是否遵循 09-ErrorHandling.md（Result<T> 模式）
- [ ] 启动流程是否遵循 10-StartupPipeline.md（Phase 0~3）
- [ ] 日志是否遵循 15-LoggingStrategy.md（结构化、CorrelationId）
- [ ] 状态管理是否遵循 08-StateManagement.md（四分法）
- [ ] 各模块文件/目录结构是否匹配文档约定

**输出文档**：`03-Review-ContractCompliance.md`

---

### 第4遍：Bug与边界条件审查

**焦点**：逐文件排查潜在Bug

**检查项**：
- [ ] 空引用风险（nullable 未检查）
- [ ] 资源泄漏（IDisposable 未正确释放、HttpClient 滥用）
- [ ] 线程安全问题（共享可变状态无锁保护）
- [ ] 异常吞噬（catch 后无 log 或重抛）
- [ ] CancellationToken 是否正确传递
- [ ] async/await 陷阱（fire-and-forget、死锁风险）
- [ ] 数据库连接/事务管理
- [ ] 文件路径处理（路径注入、权限）
- [ ] 并发下载/安装的竞态条件

**输出文档**：`04-Review-BugsAndEdgeCases.md`

---

### 第5遍：可改进项与最终总结

**焦点**：代码质量、性能、可维护性改善建议

**检查项**：
- [ ] 代码重复（DRY 原则）
- [ ] 命名一致性（与文档术语对齐）
- [ ] 性能隐患（不必要的分配、频繁 GC 压力）
- [ ] 测试覆盖度评估
- [ ] 配置硬编码（magic numbers、magic strings）
- [ ] 可读性改善机会
- [ ] 安全性（OWASP Top 10 相关）

**输出文档**：`05-Review-ImprovementsAndSummary.md`

---

## 审查流程规范

### 每轮审查前

1. 读取本计划文档 (`00-ReviewPlan.md`)
2. 读取审查日志 (`99-ReviewLog.md`)
3. 读取已完成的前序审查文档

### 每轮审查中

1. 将审查拆分为小任务逐目录/逐模块执行
2. 详细记录发现的每个问题（文件路径、行号、问题描述、严重程度、改进建议）
3. 严重程度分为：🔴 严重(违反文档/Bug) | 🟡 中等(偏离最佳实践) | 🟢 轻微(改善建议)

### 每轮审查后

1. 将所有发现写入对应轮次文档
2. 更新审查日志 (`99-ReviewLog.md`)
3. `git add` + `git commit` + `git push`

---

## 文件清单

| 文件 | 用途 |
|------|------|
| `00-ReviewPlan.md` | 本文件。审查计划和规范 |
| `01-Review-ArchitectureCompliance.md` | 第1遍审查结果 |
| `02-Review-ModuleCoupling.md` | 第2遍审查结果 |
| `03-Review-ContractCompliance.md` | 第3遍审查结果 |
| `04-Review-BugsAndEdgeCases.md` | 第4遍审查结果 |
| `05-Review-ImprovementsAndSummary.md` | 第5遍审查结果 + 最终总结 |
| `99-ReviewLog.md` | 审查日志（进度追踪） |

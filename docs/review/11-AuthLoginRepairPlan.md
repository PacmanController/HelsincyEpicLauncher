# Auth 登录正式修复实施计划

> 本文档用于承接 2026-04-17 的 Epic 登录问题分析与外部参考调研结果。  
> 目标是在不破坏 [02-ArchitecturePrinciples.md](../02-ArchitecturePrinciples.md)、[04-ModuleDependencyRules.md](../04-ModuleDependencyRules.md)、[12-AICollaboration.md](../12-AICollaboration.md)、[14-AntiPatterns.md](../14-AntiPatterns.md) 以及 [06-ModuleDefinitions/Auth.md](../06-ModuleDefinitions/Auth.md) 的前提下，分阶段恢复首次网页登录能力。  
> 本计划刻意拆成小任务，避免单次 AI 会话跨越过多模块和职责边界。

---

## 0. 当前基线

### 0.1 已完成的预修复

以下内容已经在当前仓库中落地，不再重复作为正式修复任务：

- `EpicOAuth:RedirectUri` 已配置化，默认值为 `http://localhost:6780/callback`
- OAuth 授权 URL 已包含 `state`
- 回调已补充路径校验、`state` 校验、`error` / `error_description` 解析
- `EpicOAuthProtocolTests` 已覆盖授权 URL 组装和主要错误分支
- 审查日志已记录登录失败根因与预修复状态

### 0.2 仍然阻塞首次登录恢复的核心问题

- 当前仍未掌握该 Epic 客户端真实允许的 `redirect_uri`
- 现有主链路只押注“系统浏览器 + 固定 loopback 回调”，没有第二入口
- 当前单元测试主要集中在协议解析层，尚未覆盖配置兼容性、主链路成功场景和回退入口

### 0.3 推荐的最优修复路线

1. 继续把“系统浏览器 + 固定、已验证的 `redirect_uri`”作为主登录链路
2. 如果外部环境仍然导致主链路不稳定，再增加“手动导入 authorization code”作为二级回退入口
3. 不把 SID 流、第三方辅助站点、外部 CLI 依赖纳入主方案

原因：该路线改动最小，最符合当前 Auth 模块职责边界，也最不容易引入新的跨模块耦合。

---

## 1. 本次修复必须遵守的边界

| 来源 | 约束 | 对本次修复的直接含义 |
|------|------|----------------------|
| 02-ArchitecturePrinciples | UI 只负责显示和交互 | Shell 只触发 `IAuthService`，不接管 OAuth 协议细节 |
| 02-ArchitecturePrinciples | R-02 模块间只通过 Contracts 通信 | 如果增加备用登录入口，只能升级 `IAuthService`，不能让 Presentation 直接碰 Infrastructure.Auth |
| 02-ArchitecturePrinciples | R-03 默认 internal | 新增 Auth 内部实现默认 `internal`，只在 Contracts 中公开必要接口 |
| 04-ModuleDependencyRules | P-01 / P-05 禁止跨模块引用内部实现、禁止反向依赖 | 不允许 Shell、Settings、App 直接引用 `EpicOAuthHandler` 或其他 Auth 内部类 |
| 12-AICollaboration | AI-01 单模块原则 | 每个实施任务尽量只动 Auth，只有“手动 code 回退入口”是显式契约升级 |
| 12-AICollaboration | AI-04 / AI-05 | 若升级 `IAuthService`，必须同步更新文档并列出受影响模块 |
| 14-AntiPatterns | AP-01 God Service | 禁止把主链路、回退链路、会话刷新、UI 协调揉成一个新的万能 Auth Manager |
| 14-AntiPatterns | AP-02 Page Code-Behind 写业务 | 若加备用输入框，Code-Behind 只处理纯视觉交互，业务仍走 ViewModel / `IAuthService` |
| 14-AntiPatterns | AP-05 全局静态状态横飞 | 禁止引入全局静态登录状态或一次性缓存来“绕过”现有会话链路 |
| Auth 模块文档 | OAuth 回调必须是固定、可配置、精确值 | 不再回到“端口轮询猜测”或“运行时动态拼回调地址”的旧方案 |

---

## 2. Phase 总览

| Phase | 名称 | Task 数 | 模块范围 | 性质 | 前置依赖 |
|------|------|---------|----------|------|----------|
| P0 | 外部真值确认与决策闸门 | 2 | Auth 配置 / 文档 | 分析 + 配置前置 | 无 |
| P1 | 主登录链路恢复 | 3 | Auth | 配置 + 测试 + 验证 | P0 |
| P2 | 二级回退入口 | 3 | Auth + Shell（显式契约升级） | 契约变更 | P1 可选后置 |
| P3 | 文档收口与全链路验收 | 2 | 文档 / 测试 / 验证 | 收口 | P1 完成，P2 如已实施则一起收口 |

说明：

- P0 和 P1 是必须项，没有它们就不能称为“正式修复”
- P2 是条件项，仅在主链路仍然受外部环境影响、或者需要降低运维依赖时实施
- P3 始终要做，因为每次 Auth 契约调整都必须补文档和日志

---

## 3. 详细任务拆分

## Phase P0：外部真值确认与决策闸门

### Task P0.1 — 确认可用 redirect_uri 真值

**目标**：拿到一个已验证可用的 `redirect_uri`，作为后续正式修复的唯一输入真值。

**模块范围**：无代码改动，属于 Auth 运维前置。

**必须先读**：

- `docs/06-ModuleDefinitions/Auth.md`
- `docs/review/99-ReviewLog.md` 中 2026-04-17 的 Auth 两条记录
- `src/Launcher.App/appsettings.json`

**执行内容**：

1. 确认该 `ClientId` 当前允许的 `redirect_uri` 列表或历史真值来源
2. 明确该 URI 的协议类型：
   - `http://localhost:<port>/callback`
   - 自定义协议
   - 非 loopback HTTPS
3. 把确认结果记录进审查日志，避免后续 AI 会话再次重复猜测

**完成标准**：

- 得到 1 个明确可用的 `redirect_uri`
- 明确它是否与当前 `HttpListener` 策略兼容

**为什么必须单独拆出**：

如果这一步不先做，后续任何代码改动都可能建立在错误前提上，属于无效修复。

#### P0.1 loopback 回调外部验证清单

执行 P0.1 时，不要只问“有没有可用 redirect_uri”，而是至少确认以下精确信息：

如需直接发给 Epic/外部维护方，可使用 [12-AuthRedirectInquiryTemplate.md](12-AuthRedirectInquiryTemplate.md) 中的现成模板。

1. `ClientId=34a02cf8f4414e29b15921876da36f9a` 当前是否支持任何 loopback `redirect_uri`
2. 若支持，允许值是否必须是**精确完整字符串**，还是允许有限变体
3. 允许的 scheme 是什么：
   - `http://localhost:<port>/callback`
   - `http://127.0.0.1:<port>/callback`
   - `https://localhost:<port>/callback`
   - 其他固定 loopback URI
4. 允许的 host、端口、path 是否都必须精确匹配；当前实现默认要求显式端口和固定 path
5. 浏览器交互是否可以继续使用标准 `id/authorize?redirect_uri=...`，还是必须走 `id/login?redirectUrl=...` 包装链路
6. 成功回调时实际返回的 query 字段名是否为 `code`，是否还会携带 `state`、`error`、`error_description` 之外的关键字段
7. token 交换阶段是否必须带回同一个 `redirect_uri`，以及是否需要额外参数如 `token_type=eg1`
8. 当前 client secret + Basic Auth 方式是否仍然适用于 loopback code exchange，还是需要切换为其他授权方式
9. authorization code 的有效期、单次使用语义、重放失败错误码是否稳定
10. 若当前 clientId 完全不支持 loopback，是否存在同等权限但支持 loopback 的替代 clientId；若没有，则必须明确进入非 loopback 分支

本仓库当前代码对 loopback 主链路的硬性约束如下，外部确认结果必须能满足它们，否则不能直接进入 P1：

- `EpicOAuthOptions.RedirectUri` 当前为单一固定值，默认 `http://localhost:6780/callback`
- `StartListener()` 只接受 `http` + loopback + 显式端口
- `WaitForCallbackAsync()` 要求回调 path 与配置 path 精确匹配，并依赖 `state` 校验
- 自动回调流的 token exchange 当前会回传 `redirect_uri`，但不会附带 `token_type=eg1`

建议把对外确认输出整理为一张表，再决定是否进入 P1：

| 字段 | 需要确认的值 | 当前代码假设 |
|------|--------------|--------------|
| redirect_uri | 精确完整 URI | `http://localhost:6780/callback` |
| scheme | `http` / `https` | `http` |
| host | `localhost` / `127.0.0.1` / 其他 | `localhost` |
| port | 是否固定、是否允许变更 | 固定显式端口 |
| path | 是否必须 `/callback` | `/callback` |
| authorize 入口 | `id/authorize` 或 `id/login?redirectUrl=...` | 两者尚未统一 |
| token exchange 参数 | `redirect_uri`、`token_type`、Basic Auth 要求 | 自动流当前只带 `redirect_uri` |
| code 返回字段 | `code` / `authorizationCode` / 其他 | `code` |

---

### Task P0.2 — 回调兼容性决策闸门

**目标**：根据 P0.1 的结果，决定主链路是“只改配置”还是“先做 Auth 内部回调策略抽象”。

**决策规则**：

- 若可用 URI 是 `http` + loopback + 显式端口：直接进入 P1，保持现有主链路策略
- 若可用 URI 不是 loopback HTTP：暂停 P1 的配置切换，先在 Auth 内部追加“回调接收策略抽象”设计任务，再实施代码变更

**注意**：

- 这一决策仍然只允许在 Auth 模块内部展开
- 不允许因此把网页登录协议细节泄漏到 Shell、Settings 或 App 层

**完成标准**：

- 在日志或计划文档中明确写出选择的分支
- 后续实施任务只沿着一个分支推进，不并行试错

### P0 当前执行结论（2026-04-17）

- 仓库内未找到任何已验证可用的 loopback `redirect_uri` 真值
- git 历史显示：`EpicOAuth:RedirectUri` 是 2026-04-16 预修复阶段才落入配置的默认值，早期 Auth 实现依赖的是代码内 `6780-6784` 端口猜测，而不是历史上可验证的正确配置
- 外部参考 `legendary` 对同一组客户端凭据 `34a02cf8f4414e29b15921876da36f9a` / `daafbccc737745039dffe53d94fc76cf` 的交互式登录入口，使用的是 `https://www.epicgames.com/id/login?redirectUrl=https://www.epicgames.com/id/api/redirect?clientId=34a02cf8f4414e29b15921876da36f9a&responseType=code`
- `https://legendary.gl/epiclogin` 的实际跳转目标也指向同一 Epic HTTPS 重定向端点，而不是 localhost 回调
- 因此，当前可确认的外部真值不是 loopback HTTP + 显式端口，而是 Epic 托管的 HTTPS 重定向链路
- 基于该结论，P0.2 的分支已经锁定：**停止“仅替换 loopback 配置值即可恢复登录”的路线**
- 后续应先在 Auth 模块内部设计“非 loopback 浏览器结果接收策略”，再进入正式代码修复；该策略可以是手动 authorization code 导入，或等价的 Auth 内部结果接收抽象，但不得把协议细节泄漏到 Shell / Settings / App

### P0 后的最小实现策略（2026-04-17）

#### 选型结论

采用“两步式 authorization code 导入”作为当前 clientId 的正式恢复路线，不引入 WebView 登录、不引入 App 自定义协议注册，也不依赖第三方 CLI。

#### 选择理由

1. 外部成熟实现 `legendary` 对当前 clientId 的交互式登录，本质上是让用户拿到 authorization code 或 exchange code，再调用 token 接口建立会话
2. 当前仓库的 `IAuthService.LoginAsync()` 是单步 loopback 回调模型，已经与外部可验证链路不一致
3. Shell 当前只有一个登录按钮，没有输入框；而 Presentation 已有统一对话框服务，可在不污染页面结构的情况下补一个窄输入交互
4. 该方案改动集中在 Auth 和 Shell，符合单模块优先、最小契约升级、最小 UI 扰动三项要求

#### 推荐实现形态

1. 将登录交互拆成两个 Auth 契约方法：
   - 第一步：启动浏览器并进入 Epic 登录页
   - 第二步：提交用户粘贴的 authorization code 或 JSON 文本，完成 token 交换和会话建立
2. Shell 保留现有“登录 Epic Games”按钮，不新增常驻输入框
3. 用户点击登录后：
   - Auth 打开 Epic 登录页
   - Presentation 弹出输入对话框，请用户粘贴 authorization code 或完整 JSON 响应
   - ViewModel 将原始输入直接交回 Auth，UI 不解析协议字段
4. Auth 内部负责：
   - 构建 Epic 登录 URL
   - 解析用户输入中的 authorization code
   - 执行 token 交换
   - 获取账户信息并保存会话

#### 明确不选的方案

- 不选 WebView2 嵌入式登录：实现面更大，UI 与协议耦合更重
- 不选 App 自定义协议回调：涉及 App 层注册与启动恢复，当前不是最小修复路径
- 不选直接引入 SID / exchange token UI：会把当前任务扩大到新的协议分支

#### 当前建议的文件落点

| 文件 | 预期改动 |
|------|----------|
| `src/Launcher.Application/Modules/Auth/Contracts/IAuthService.cs` | 将单步登录升级为两步式窄接口 |
| `src/Launcher.Infrastructure/Auth/AuthService.cs` | 编排“打开登录页”和“提交 code 完成登录”两段流程 |
| `src/Launcher.Infrastructure/Auth/EpicOAuthHandler.cs` | 新增 Epic 登录 URL 构建、浏览器打开、authorization code 输入解析、非 loopback code 交换能力 |
| `src/Launcher.Presentation/Shell/IDialogService.cs` | 增加窄输入对话框能力，避免落回未实现的泛型自定义弹窗 |
| `src/Launcher.Presentation/Shell/DialogService.cs` | 用 `ContentDialog + TextBox` 实现 code 输入对话框 |
| `src/Launcher.Presentation/Shell/ShellViewModel.cs` | 登录命令改为“两步式交互”，并通过对话框收集用户输入 |
| `src/Launcher.Presentation/Shell/ShellPage.xaml` | 预计无需结构改动，除非后续需要补充提示文本 |

#### 协议层注意事项

- 当前 `EpicOAuthHandler` 的交互式授权入口是 `https://www.epicgames.com/id/authorize` + `redirect_uri`，与外部成熟链路不一致
- 当前 `ExchangeCodeAsync()` 会发送 `redirect_uri`
- 外部参考 `legendary` 的 authorization code 交换使用的是 `grant_type=authorization_code`，并附带 `token_type=eg1`，没有显式传入 loopback `redirect_uri`
- 因此正式实现时，应把“loopback OAuth code exchange”和“当前 clientId 的 authorization code 导入 exchange”视为两种不同的内部策略，不要强行复用同一组请求参数

---

## Phase P1：主登录链路恢复

### Task P1.1 — 将主链路切换到已验证的 redirect_uri

**目标**：在保持当前登录模型不变的前提下，把主链路对准真实可用的 `redirect_uri`。

**模块范围**：Auth 配置和必要的 Auth 内部实现。

**默认触达文件**：

- `src/Launcher.App/appsettings.json`
- `src/Launcher.Infrastructure/Auth/EpicOAuthOptions.cs`
- `src/Launcher.Infrastructure/Auth/EpicOAuthHandler.cs`

**执行内容**：

1. 将 `EpicOAuth:RedirectUri` 切换为 P0.1 确认的值
2. 若 URI 仍然兼容 loopback 监听：只做最小配置切换，不新增无关抽象
3. 若 URI 触发 P0.2 的非 loopback 分支：只在 Auth 内部引入最小回调接收策略，不扩散到其他模块

**禁止事项**：

- 不新增 Settings 页配置入口；这不是终端用户偏好项
- 不引入外部辅助站点作为主方案依赖
- 不创建新的万能 `AuthManager` / `LoginCoordinator`

**完成标准**：

- 主链路使用的唯一回调地址来自配置
- 代码中不存在新的“猜测端口”或“多地址轮询”逻辑

---

### Task P1.2 — 补足 Auth 主链路自动化验证

**目标**：把当前仅覆盖协议解析层的测试，扩展到“配置 + 回调接收 + 结果落库链路”的关键断点。

**模块范围**：Auth 测试和必要的 Auth 内部可测试性改造。

**建议新增覆盖点**：

1. `EpicOAuthOptions` 读取已配置 `RedirectUri`
2. `StartListener()` 对非法 URI、缺少端口、非 loopback URI 的拒绝分支
3. 成功 token 交换后 `AuthService` 的会话存储与用户信息更新
4. 登录失败时错误码和日志上下文不被吞掉

**建议文件**：

- `tests/Launcher.Tests.Unit/EpicOAuthProtocolTests.cs` 扩展已有协议测试
- 新增 `tests/Launcher.Tests.Unit/EpicOAuthOptionsTests.cs`
- 新增 `tests/Launcher.Tests.Unit/AuthServiceLoginTests.cs`

**完成标准**：

- 至少覆盖 1 条成功主链路和 3 条关键失败分支
- 新增测试只围绕 Auth，不跨到无关模块

---

### Task P1.3 — 主链路人工验收

**目标**：在真实桌面环境中证明“首次网页登录”已经恢复，而不是只靠单元测试判断。

**人工验证步骤**：

1. 启动应用，点击登录 Epic Games
2. 浏览器完成授权后，应用收到回调
3. Shell 登录态变为已登录，显示用户信息
4. 重启应用，`TryRestoreSessionAsync()` 恢复会话成功
5. 执行登出，会话和本地 token 被清理

**完成标准**：

- 首次登录成功
- 重启恢复成功
- 登出后不残留旧会话

---

## Phase P2：二级回退入口（条件项）

> 只有在 P1 完成后仍存在外部依赖不稳定、或需要为用户提供更强恢复能力时才做。  
> P2 属于显式契约升级，必须同步更新接口文档与受影响模块说明。

### Task P2.1 — 升级 IAuthService，增加手动 code 导入能力

**目标**：给 Auth 增加第二条窄入口，但仍由 Auth 自己完成 token 交换和会话建立。

**建议接口方向**：

新增一个窄方法，语义聚焦在“导入 authorization code”，而不是抽象成宽泛的万能登录接口。

**受影响模块**：

- Auth.Contracts
- Auth.Infrastructure
- Shell / Presentation
- 相关单元测试
- `docs/05-CoreInterfaces.md`
- `docs/06-ModuleDefinitions/Auth.md`

**完成标准**：

- 新增入口不破坏现有 `LoginAsync()` 主语义
- 契约命名清晰，没有出现万能接口趋势

---

### Task P2.2 — Auth 内部实现手动 code 交换链路

**目标**：复用现有 token 交换、用户信息加载、会话保存逻辑，不重新发明第二套 Auth 流程。

**模块范围**：Auth。

**执行内容**：

1. 在 `AuthService` 中新增“导入 authorization code”入口
2. 让该入口复用 `EpicOAuthHandler` 的 code exchange 能力
3. 复用现有会话保存、用户信息加载、刷新链路

**禁止事项**：

- 不直接引入 SID 流
- 不引入 Heroic / Legendary 的外部网页或 CLI 依赖
- 不复制一套平行的 token 存储逻辑

**完成标准**：

- 手动 code 导入和网页登录最终进入同一会话落库链路
- 失败时返回结构化 Result，不吞错误

---

### Task P2.3 — Presentation 增加手动 code 回退入口

**目标**：只在 Presentation 增加纯 UI 层的备用输入入口，业务仍由 `IAuthService` 处理。

**模块范围**：Shell / Presentation。

**建议表现**：

- 登录按钮附近增加“备用登录”入口，默认低优先级
- 用户粘贴 authorization code 后，由 ViewModel 调用新的 Auth 契约方法
- 不在 Code-Behind 中写 token 交换或浏览器逻辑

**完成标准**：

- 备用入口不污染主界面流程
- Code-Behind 仅有纯视觉逻辑，业务仍在 ViewModel 和 Auth 中

---

## Phase P3：文档收口与全链路验收

### Task P3.1 — 同步更新文档与日志

**目标**：避免 Auth 正式修复后再次出现“代码已变、文档还停在旧设计”的漂移。

**至少同步**：

- `docs/06-ModuleDefinitions/Auth.md`
- `docs/05-CoreInterfaces.md`（若 P2 发生契约升级）
- `docs/review/99-ReviewLog.md`

**完成标准**：

- 文档中的首次登录、回退入口、验证结论与实际代码一致
- 审查日志能说明修复范围、架构边界和验证结果

---

### Task P3.2 — 执行最终验证矩阵

**目标**：用固定矩阵确认修复没有引入回归，也没有突破架构边界。

**最低通过条件**：

1. `dotnet build HelsincyEpicLauncher.slnx` 通过
2. 相关单元测试全部通过
3. 首次登录成功
4. 重启会话恢复成功
5. 登出成功
6. 日志中没有再把 provider 失败错误错误折叠为“用户取消”

---

## 4. 验证矩阵

| 编号 | 场景 | 前置条件 | 执行动作 | 期望结果 | 覆盖层 |
|------|------|----------|----------|----------|--------|
| V1 | RedirectUri 配置生效 | `appsettings.json` 配置目标 URI | 启动登录流程 | 日志打印实际使用的固定 URI，且与配置一致 | 自动化 + 人工 |
| V2 | 非法 RedirectUri 被拒绝 | 把 URI 改成非法值 | 触发登录 | 返回配置错误，不启动无效监听 | 自动化 |
| V3 | 非 loopback URI 决策分支正确 | P0.1 结果为非 loopback | 执行 P0.2 | 明确停止“仅改配置”路线，转入 Auth 内部策略抽象 | 文档 + 审查 |
| V4 | 回调路径错误 | 构造错误回调路径 | 调用回调解析 | 返回 `AUTH_CALLBACK_PATH_INVALID` | 自动化 |
| V5 | state 不匹配 | 构造错误 `state` | 调用回调解析 | 返回 `AUTH_CALLBACK_STATE_INVALID` | 自动化 |
| V6 | Provider 返回 invalid_redirect_url | 构造 `error=invalid_redirect_url` | 调用回调解析 | 返回 `AUTH_CALLBACK_PROVIDER_ERROR` 且用户消息明确指出回调地址配置问题 | 自动化 |
| V7 | 首次网页登录成功 | 使用已验证的可用 URI | 点击登录并完成授权 | Shell 进入已登录态，显示用户信息 | 人工 |
| V8 | 登录成功后会话恢复 | 已有有效 token | 重启应用 | `TryRestoreSessionAsync()` 恢复成功 | 人工 + 自动化 |
| V9 | 登出清理完整 | 当前为已登录态 | 点击登出 | 内存态和本地 token 清空，UI 回到未登录态 | 人工 + 自动化 |
| V10 | 手动 code 回退成功 | P2 已实施，拿到有效 authorization code | 通过备用入口提交 code | 建立会话成功，后续刷新/恢复链路正常 | 人工 + 自动化 |
| V11 | 手动 code 回退失败 | P2 已实施，输入无效 code | 提交 code | 返回结构化错误，不污染已有会话 | 自动化 |
| V12 | 架构边界未破坏 | 修复完成 | 代码审查 | 没有 Presentation 直连 Auth 内部实现，没有新增万能服务，没有全局静态登录状态 | 审查 |

---

## 5. 建议的执行顺序

为了适应上下文限制，建议按以下最小步长推进：

1. 先做 P0.1，确认外部真值
2. 立刻做 P0.2，锁定分支
3. 若仍是 loopback HTTP，直接做 P1.1
4. 再做 P1.2，补自动化验证
5. 之后做 P1.3，完成人工验收
6. 只有在 P1 仍不足以稳定恢复登录时，才进入 P2
7. 最后统一做 P3 文档与验证收口

每个任务完成后都应更新 `docs/review/99-ReviewLog.md`，不要等到全部完成后再补记。

---

## 6. 明确不纳入本轮主修复的事项

以下内容有研究价值，但不应混入本轮“最小可行正式修复”：

- 基于 SID 的替代登录流
- 对第三方辅助站点的直接依赖
- 引入外部 CLI 作为登录核心链路
- 把 OAuth 运行配置做成终端用户可编辑设置项
- 顺手重构 Token 存储到 Credential Locker

这些事项会扩大影响面，应在主链路恢复后再作为独立任务评估。
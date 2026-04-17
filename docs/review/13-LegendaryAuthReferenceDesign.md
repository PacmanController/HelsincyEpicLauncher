# 基于 Legendary 参考实现的 Auth 下一阶段设计说明

> 本文档承接 [11-AuthLoginRepairPlan.md](11-AuthLoginRepairPlan.md) 的 P0 结论、当前仓库已经落地的两步式 authorization code 登录止血方案，以及对 `legendary-gl/legendary` 登录实现的代码级分析。  
> 目标不是复制 Legendary 的 CLI/UI 形态，而是提炼它已经被验证过的 Epic 登录链路事实，并把这些事实收敛成符合本仓库架构边界的下一阶段设计。  
> 本设计必须同时服从 [02-ArchitecturePrinciples.md](../02-ArchitecturePrinciples.md)、[04-ModuleDependencyRules.md](../04-ModuleDependencyRules.md)、[12-AICollaboration.md](../12-AICollaboration.md)、[14-AntiPatterns.md](../14-AntiPatterns.md)、[06-ModuleDefinitions/Auth.md](../06-ModuleDefinitions/Auth.md) 与 [06-ModuleDefinitions/Shell.md](../06-ModuleDefinitions/Shell.md)。

---

## 0. 一页结论

1. Legendary 不能证明“当前 clientId 的主链路应该是 localhost loopback 回调”；相反，它证明这组客户端凭据的成熟交互链路更接近 Epic 托管登录页 + Epic HTTPS 重定向端点。
2. Legendary 的可验证登录能力分成三类：
   - `authorization_code`：手动导入 authorization code
   - `exchange_code`：嵌入式浏览器/WebView 桥接拿 exchange code
   - `refresh_token`：从现有 Epic Launcher 会话导入 refresh token
3. 因此，本仓库的下一阶段不应继续把 loopback 当成唯一设计中心；更合理的路线是：
   - 保留当前已经可用的“系统浏览器 + authorization code / 回调 URL 兜底”
   - 在 Auth 模块内部先完成“登录结果归一化 + grant 执行策略”抽象
   - 再视收益和风险，决定是否增加 WebView2 exchange code 路径或 EGL refresh token 导入路径
4. `Launcher.App` 当前已经打通“外部回调候选负载 → Auth 完成登录”的宿主骨架，这条链路应继续保留，但它不再是下一阶段设计的主假设，只是未来若拿到真实回调来源时可直接复用的入口。

---

## 1. 设计目标与非目标

### 1.1 设计目标

- 把 Auth 的下一阶段设计建立在已验证的外部实现事实之上，而不是继续猜测 `redirect_uri`
- 保持 UI 只负责触发和收集输入，不让 Shell / App 接管 OAuth 协议细节
- 为未来接入 `exchange_code` 或外部会话导入预留扩展点，但不提前把跨模块契约做成大而泛的万能接口
- 明确日志语义和记录边界，避免后续运行态继续出现“链路已经失败，但日志只显示登录取消”的信息折叠

### 1.2 非目标

- 不在本阶段直接实现 WebView2 登录
- 不在本阶段直接接入 Epic Launcher refresh token 导入
- 不在本阶段重新扩大 Shell 页面结构或增加常驻输入框
- 不把 SID 登录当成主路径
- 不把第三方站点、外部 CLI 或脚本进程作为正式运行时依赖

---

## 2. 外部参考事实

以下内容来自对 `legendary-gl/legendary` 的代码级分析，而不是二手描述：

| 主题 | 事实 | 对本仓库的含义 |
|------|------|----------------|
| 浏览器登录入口 | Legendary 交互式登录会打开 `https://www.epicgames.com/id/login?redirectUrl=https://www.epicgames.com/id/api/redirect?clientId=...&responseType=code` | 当前这组 clientId 的成熟链路并不是“浏览器回 localhost” |
| 手动兜底 | 禁用 webview 或桥接不可用时，Legendary 让用户粘贴 `authorizationCode` | 当前仓库已实现的手动 authorization code 兜底方向是合理的 |
| 嵌入式浏览器 | `webview_login.py` 通过脚本桥接拿到 `exchange_code`，再走 `grant_type=exchange_code` | 如果未来要减少人工输入，真正应评估的是 exchange code 路线，而不是继续猜 loopback |
| 会话复用 | Legendary 可从 EGL 导入 refresh token | 本仓库未来若要做“免再次网页登录”，可以考虑外部会话导入，而非只盯着浏览器回调 |
| grant 支持 | `authorization_code`、`exchange_code`、`refresh_token`、`client_credentials` 都是已存在的真实分支 | Auth 内部应准备“同一登录终态，不同 grant 执行策略”的组织方式 |
| SID 路线 | 仓库里虽然保留 SID 相关逻辑，但不是稳定主链 | 不应在本仓库把 SID 当作优先方案 |

---

## 3. 当前本地基线

当前仓库已经落地并验证过的事实如下：

1. `IAuthService` 已拆成 `StartAuthorizationCodeLoginAsync()` 与 `CompleteAuthorizationCodeLoginAsync(...)` 两段式交互。
2. Shell 默认登录路径不再接受整段 JSON，人工继续登录只接受 authorization code 或完整回调 URL。
3. `Launcher.App` 已能把首实例启动参数或第二实例消息中的“回调 URL 候选负载”自动转交给 Auth。
4. 真实运行态已经证明宿主转发链路可进入 `CompleteAuthorizationCodeLoginAsync(...)`；失败时若 code 是伪造值，会正确停在 token exchange 阶段。
5. 当前实现仍以 `authorization_code` 为中心组织命名和契约，尚未为 `exchange_code` 或外部 refresh token 导入提供明确的内部抽象。

这意味着：

- 当前代码不是“没有登录能力”，而是“已经有一个可工作的 fallback 主线，但还没有为后续更成熟的结果来源做好结构化扩展”。

---

## 4. 架构约束映射

| 约束来源 | 必须遵守的点 | 本设计中的直接落实 |
|---------|--------------|--------------------|
| 02-ArchitecturePrinciples | UI 只负责显示和交互 | Shell 只触发登录、收集用户输入、宿主 WebView2 容器；协议解析和 token exchange 均留在 Auth |
| 02-ArchitecturePrinciples / R-02 | 模块间只通过 Contracts 通信 | Presentation 不直接引用 `EpicOAuthHandler`、`EpicOAuthProtocol` 或未来的 Auth 内部策略类 |
| 02-ArchitecturePrinciples / R-08 | 禁止万能 Service | 不引入新的 `AuthManager` / `LoginCoordinator` 大一统类；改为在 Auth 内部拆窄职责组件 |
| 04-ModuleDependencyRules / P-01 | 禁止跨模块引用内部实现 | App 只做回调负载转发；Shell 只依赖 `IAuthService` 和 `IDialogService` |
| 04-ModuleDependencyRules / P-05 | 禁止反向依赖 | Auth 不依赖 Presentation；未来 WebView2 若需要桥接，也由 Presentation 收集结果后再回调 Contracts |
| 14-AntiPatterns / AP-02 | 禁止 Page Code-Behind 写业务 | 即使后续增加 WebView2 容器，Code-Behind 也只做视觉/宿主事件转发 |
| 14-AntiPatterns / AP-05 | 禁止全局静态登录状态 | 不新增全局静态 code/token 缓存来绕过现有会话链路 |
| 06-ModuleDefinitions/Auth | 回调或授权结果处理必须留在 Auth 内部 | 无论 payload 来源是回调 URL、authorization code、exchange code 还是 refresh token 导入，最终都在 Auth 模块里归一处理 |
| 06-ModuleDefinitions/Shell | Shell 只作为壳层 | Shell 不新增协议状态机，只承载统一对话框或未来的登录容器 |

---

## 5. 推荐的目标结构

### 5.1 近期原则：先不做公共契约大改名

当前已经上线的两步式 `authorization_code` 契约可以继续保留一个迭代，原因是：

1. 它已经与当前浏览器 + 人工兜底流程匹配
2. 现阶段若立刻把 Contracts 改成泛化的“交互式登录万能接口”，会提前扩大跨模块影响面
3. 根据 AI-01 / AI-04，当前更合理的是先把扩展点收敛在 Auth 内部，再在真正接入 `exchange_code` 或会话导入时做一次明确的契约升级

因此，下一实现批次建议：

- `IAuthService` 维持现状
- 仅在 Auth 内部引入“登录结果归一化”和“grant 执行策略”

### 5.2 Auth 内部抽象建议

建议只在 `Launcher.Infrastructure.Auth` 内部引入以下概念：

```csharp
internal enum EpicLoginResultKind
{
    AuthorizationCode,
    CallbackUrl,
    ExchangeCode,
    ExternalRefreshToken,
}

internal sealed record EpicLoginResult(
    EpicLoginResultKind Kind,
    string Payload,
    string Source);

internal interface IEpicLoginGrantExecutor
{
    EpicLoginResultKind Kind { get; }
    Task<Result<TokenPair>> ExecuteAsync(EpicLoginResult input, CancellationToken ct);
}
```

职责拆分建议：

| 组件 | 位置 | 职责 |
|------|------|------|
| `EpicOAuthProtocol` | Infrastructure.Auth | 继续负责 URL 构建、回调/手工输入解析、payload 归一化 |
| `IEpicLoginGrantExecutor` 系列 | Infrastructure.Auth | 按 grant 类型执行 token exchange，而不是把所有分支塞进一个方法 |
| `AuthService` | Infrastructure.Auth | 只负责编排：启动登录、提交结果、保存会话、发布事件 |
| `Launcher.App` | App | 只负责把候选回调负载转交 Auth，不参与 grant 选择 |

第一批推荐只实现一个执行器：

- `AuthorizationCodeGrantExecutor`

并在内部为后续保留：

- `ExchangeCodeGrantExecutor`
- `ExternalRefreshTokenGrantExecutor`

这样可以避免 AP-01 的 God Service，也不会把未来分支提前扩散到 UI。

### 5.3 Presentation / Shell 职责边界

Shell 下一阶段仍只承担两类输入来源：

1. 当前已存在的“高级：手动继续登录”文本输入
2. 若未来立项 WebView2，则只承载“浏览器容器 + 结果回传”

明确禁止：

- Shell 自己解析 `authorizationCode`
- Shell 判断该走哪种 grant
- Shell 保存任何临时 token / code
- App 或 Shell 为了兼容未来路径而增加全局共享登录状态

### 5.4 公共契约升级触发条件

只有在以下场景真正进入实现时，才建议升级 `IAuthService`：

1. 接入 WebView2，需要明确提交 `exchange_code`
2. 接入 EGL 会话导入，需要明确提交外部 refresh token

届时推荐升级为窄 DTO，而不是继续新增一串并列方法：

```csharp
public enum AuthLoginCompletionKind
{
    AuthorizationCode,
    CallbackUrl,
    ExchangeCode,
    ExternalRefreshToken,
}

public sealed class AuthLoginCompletionInput
{
    public AuthLoginCompletionKind Kind { get; init; }
    public string Payload { get; init; } = default!;
}
```

推荐理由：

- 满足 04 文档要求的窄 DTO
- 避免未来每加一种结果来源就再膨胀一个新方法
- 比“继续让 UI 传一段无类型字符串，然后由 Auth 猜测”更可维护

但这一升级不应提前做；只有真正进入 `exchange_code` 或外部导入实现时才触发。

---

## 6. 日志设计

日志是本设计的硬要求，不是补充项。后续实现时必须明确记录“来源、阶段、grant、结果”，同时禁止写入原始敏感值。

### 6.1 分层日志边界

| 层 | 允许记录什么 | 禁止记录什么 |
|----|--------------|--------------|
| Shell | 用户点击登录、是否打开高级入口、用户取消输入、UI 错误提示是否展示 | 原始 authorization code、完整回调 URL query、任何 token |
| App | 是否收到候选回调负载、来源是首实例参数还是第二实例消息、是否成功转交 Auth | 原始 code、完整 URL 中的敏感 query |
| Auth | 归一后的输入类型、选择的 grant、token exchange 成败、provider 错误摘要、会话建立/恢复/过期 | 原始 code、access token、refresh token、未脱敏响应体 |

### 6.2 推荐日志事件

| 事件名 | 层 | 必要字段 |
|--------|----|----------|
| `Auth login started` | Shell/Auth | `mode=system_browser_manual_fallback` |
| `Auth login result received` | Auth | `kind`, `source`, `normalized=true/false` |
| `Auth token exchange started` | Auth | `grant_type` |
| `Auth token exchange failed` | Auth | `grant_type`, `status_code`, `provider_error_code` |
| `Auth token exchange succeeded` | Auth | `grant_type`, `expires_at` |
| `Auth session restored` | Auth | `source=cached_refresh_token` |
| `Auth callback payload forwarded` | App | `source=second_instance|launch_args` |

### 6.3 日志脱敏规则

必须继续复用 `LogSanitizer`，并额外满足：

1. 不记录原始 `authorizationCode`
2. 不记录原始 `exchange_code`
3. 不记录完整回调 URL；若确需定位，只记录 path 和 query key 名，不记录 value
4. 不记录 access token / refresh token
5. Provider 返回体如果需要诊断，只保留脱敏后的错误码和错误消息摘要

---

## 7. 实施顺序建议

### Phase L0：文档定稿与日志归档

目标：把 Legendary 参考结论固定下来，避免后续再次回到“继续猜 loopback”状态。

本阶段输出：

- 本文档
- `docs/review/99-ReviewLog.md` 对应日志条目
- `SESSION_HANDOFF.md` 当前主线更新

### Phase L1：Auth 内部结果归一化

目标：不改公共契约，仅在 Auth 内部引入 `EpicLoginResultKind` 和 grant 执行器。

范围：

- `Launcher.Infrastructure.Auth`
- 必要时补 `Launcher.Tests.Unit` 中的 Auth 单测

不做：

- 不新增 WebView2
- 不修改 Shell UI 结构
- 不扩大 App 职责

验收标准：

- 当前 `authorization_code` 手工兜底链路行为保持不变
- 日志改为按“来源/类型/grant/结果”记录
- 为未来 `exchange_code` 增加执行器时不需要再重写当前 authorization code 主线

### Phase L2：是否立项 WebView2 exchange code

触发条件：用户确认要继续压缩人工输入步骤。

立项前必须回答：

1. 当前项目是否允许引入 WebView2 登录容器
2. Epic 登录页在 WinUI 3 WebView2 中是否能稳定拿到 exchange code
3. JS 桥接是否会把协议细节不当泄漏到 Presentation

若答案不稳，不进入实现。

### Phase L3：是否立项 EGL refresh token 导入

触发条件：用户优先级更偏“复用现有 Epic 桌面登录态”，而不是“浏览器内自动回跳”。

前提：

- 先做清晰的风险评估和存储来源确认
- 明确只做导入，不直接依赖外部 Launcher 进程作为运行时前置

### Phase L4：若未来拿到真实回调来源，复用现有宿主骨架

若未来 Epic 明确提供可用 loopback 或协议回调来源：

- 直接把 payload 接入现有 `Launcher.App` 转发骨架
- 仍然交由 Auth 内部的结果归一化与 grant 执行策略处理

也就是说，宿主骨架保留，但不再主导整体设计方向。

---

## 8. 下一轮实施前的验收清单

开始做代码前，先确认以下四点：

1. 本轮是否只改 Auth 内部，不动公共契约
2. 本轮是否把日志事件名和脱敏规则一起落地
3. 若要动 Shell，是否只是现有高级入口的轻微文案/交互调整，而不是新增协议逻辑
4. 若要立项 WebView2 或 EGL 导入，是否明确声明为新的跨模块契约变更任务

若这四点中有任意一项回答不清楚，就不应该直接开始编码。
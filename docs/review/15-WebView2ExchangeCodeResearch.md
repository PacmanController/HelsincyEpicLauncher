# WebView2 exchange code 预研

> 本文档承接 [13-LegendaryAuthReferenceDesign.md](13-LegendaryAuthReferenceDesign.md) 中的 Phase L2，目标是回答两个问题：
> 1. `exchange_code` 路线是否真的需要嵌入式浏览器；
> 2. 如果本仓库未来要接 WebView2，应该把它放在哪一层，以及它究竟在解决什么问题。

---

## 0. 一页结论

1. WebView2 不是恢复 Epic 登录能力的必要条件。当前仓库已经有“系统浏览器 + authorization code / 回调 URL 兜底”可用链路；Legendary 在 WebView 不可用时也走这条路。
2. WebView2 真正解决的是 UX 自动化，而不是协议可行性：它的价值是把“人工复制 authorization code”替换成“在嵌入式浏览器内直接拿 exchange code 并立即完成 token exchange”。
3. Legendary 的 WebView 登录是可选能力，不是主前提：
   - 有 `pywebview` 时，走嵌入式浏览器并拿 `exchange_code`
   - 无 `pywebview` 或显式 `--disable-webview` 时，退回系统浏览器 + 手工 `authorizationCode`
4. 对本仓库来说，这意味着 WebView2 应被定义为“压缩人工步骤的增强路径”，而不是“当前登录坏了所以必须补的底层能力”。
5. 当前仓库没有现成 WebView2 依赖、控件容器或契约入口；若要实现，它将是一次明确的跨模块契约变更，不是给现有 Auth 再补一个内部 if 分支就能收口。
6. 如果未来立项，第一版只应做 `exchange_code`，不应同时引入 SID、外部 CLI 进程依赖或新的协议回调宿主方案。

---

## 1. 外部事实：Legendary 的 exchange code 路线到底是什么

### 1.1 WebView 在 Legendary 中是可选项

Legendary README 明确把 `pywebview` 标成 optional dependency；CLI `auth` 也支持：

- `--disable-webview`

实际行为是：

1. 如果 WebView 可用且未禁用，则尝试嵌入式登录
2. 否则打开外部浏览器，并让用户手工复制 `authorizationCode`

这说明 WebView 不是协议必要条件，而是交互增强能力。

### 1.2 没有 WebView 时，Legendary 仍然能登录

Legendary 在无 WebView 时会：

- 打开 `https://legendary.gl/epiclogin`
- 最终把用户带到 Epic 托管的登录/重定向链路
- 让用户从 JSON 响应里复制 `authorizationCode`
- 再走 `grant_type=authorization_code`

这和本仓库当前已经落地的人工兜底思路一致，也说明“先有可工作的浏览器链路，再考虑自动化”是合理顺序。

### 1.3 有 WebView 时，Legendary 拿的是 exchange code，不是 redirect code

Legendary 的 WebView 路线并不是“在嵌入浏览器里等待 localhost 回调”，而是：

1. 打开 Epic 登录页
2. 向页面注入桥接脚本
3. 通过页面暴露的 `window.ue.signinprompt.*` 能力拿到 `exchange_code`
4. 立即调用 `auth_ex_token(exchange_code)`
5. 最终通过标准 token endpoint 发起：
   - `grant_type=exchange_code`
   - `exchange_code=<captured code>`
   - `token_type=eg1`

也就是说，WebView 路线的协议核心并不是“嵌入浏览器”本身，而是“拿到 exchange code 这一类不同的登录结果来源”。

### 1.4 WebView 路线不依赖 loopback

Legendary 的 WebView 登录不需要：

- localhost listener
- 自定义协议回调
- 应用宿主接收浏览器重定向

它直接在嵌入页面里完成结果捕获。所以如果本仓库未来做 WebView2，它和当前 `Launcher.App` 已有的“外部回调候选负载转发骨架”是并行路线，不是同一条链的不同实现。

### 1.5 Windows 上还伴随 cookie / 会话副作用处理

Legendary 的 WebView 登录在 Windows 上会先打开 logout URL，再跳回登录页，目的就是清理嵌入浏览器中残留的 Epic 登录 cookie。

这带来两个直接结论：

1. WebView 路线不只是“塞一个浏览器控件进去”，还涉及 cookie / 会话生命周期处理
2. 即使实现成功，也可能对当前 Epic 登录态产生副作用，需要明确用户感知和运行态验证

---

## 2. 本仓库当前基线

### 2.1 当前登录链路已经可用

本仓库当前已具备：

1. 系统浏览器打开 Epic 登录页
2. 用户手工提交 `authorizationCode` 或完整回调 URL
3. Auth 内部统一归一化并执行 `authorization_code` grant
4. `Launcher.App` 宿主可转交外部回调候选负载

所以 WebView2 要解决的不是“有没有登录能力”，而是“是否值得再投入一条更自动化但更重的交互路径”。

### 2.2 当前仓库没有现成 WebView2 基建

本轮仓库内检索的结果是：

- 没有 `Microsoft.Web.WebView2` 相关依赖
- 没有 `WebView2` / `CoreWebView2` 控件或宿主封装
- 没有现成的嵌入登录页面/对话框

因此若立项，至少要补：

- Presentation 层的浏览器容器
- 与 Auth 契约配套的登录结果传递方式
- 运行态验证与回退策略

### 2.3 当前公共契约尚未为 exchange code 做准备

当前 `IAuthService` 只暴露：

- `StartAuthorizationCodeLoginAsync(...)`
- `CompleteAuthorizationCodeLoginAsync(...)`

这套契约是围绕 `authorization_code` / 回调 URL 设计的。根据 [13-LegendaryAuthReferenceDesign.md](13-LegendaryAuthReferenceDesign.md) 的既有结论，只有在真正进入 `exchange_code` 或外部 refresh token 实现时，才值得把 completion 升级为窄 DTO。

也就是说：

- WebView2 一旦立项，就已经不是“纯内部改造”
- 它会触发一次明确的 Auth 公共契约升级任务

### 2.4 App 宿主骨架不是 WebView2 的前置条件

当前 `Launcher.App` 的价值在于：

- 处理外部回调 URL 候选负载

但 WebView2 路线按 Legendary 事实并不依赖外部回调来源，因此第一版 WebView2 并不需要优先改 App 宿主。它的主要落点会是：

- Presentation 登录容器
- Auth completion 契约
- Infrastructure.Auth 的 `exchange_code` grant 执行器

---

## 3. 推荐的职责边界

### 3.1 Presentation 应承担什么

如果未来实现 WebView2，Presentation 只应负责：

- 承载登录容器
- 初始化 WebView2
- 接收页面桥接回传的原始 `exchange_code`
- 把结果交回 `IAuthService`
- 显示取消、失败和风险提示

Presentation 明确不应负责：

- token exchange
- 账户信息获取
- 解析 provider 错误响应
- 持久化任何 token / code

### 3.2 Auth 应承担什么

Auth 应继续作为协议收口层，负责：

- 构建与当前 clientId 匹配的登录入口 URL
- 接收类型化 completion 输入
- 根据 `Kind=ExchangeCode` 选择 grant executor
- 执行 `grant_type=exchange_code`
- 保存会话并发布认证事件

推荐方向仍然是 [13-LegendaryAuthReferenceDesign.md](13-LegendaryAuthReferenceDesign.md) 已提出的窄 DTO：

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

### 3.3 App 不应被提前卷入

第一版 WebView2 登录不应要求：

- 改协议激活
- 改单实例转发模型
- 新增宿主级网页登录状态管理

除非后续证明 WinUI 3 / WebView2 集成必须经由 App 宿主特殊处理，否则不要把实现面无故扩大到 `Launcher.App`。

---

## 4. 主要风险

### R-01 Epic 页面桥接能力可能漂移

Legendary 的 WebView 路线依赖页面侧 `window.ue.signinprompt.*` 桥接点。这个能力不受本仓库控制，未来 Epic 登录页一旦调整，WebView2 自动化链路可能直接失效。

要求：

- 不把 WebView2 作为唯一主登录入口
- 必须保留现有系统浏览器 + 人工兜底链路

### R-02 嵌入浏览器环境与系统浏览器并不等价

验证码、单点登录、第三方登录、cookie 行为在 WebView2 中可能与外部浏览器不同。

要求：

- 在真实 WinUI 3 运行态里验证，不要只凭协议代码推断可行
- UI 必须允许用户取消并回退到现有浏览器链路

### R-03 可能需要特定 User-Agent / 登录环境模拟

Legendary 在 WebView 登录时显式传入接近 Epic Games Launcher 的 User-Agent。若不做这类对齐，页面是否仍暴露 exchange code 桥接能力并不确定。

要求：

- 在立项实现前先做最小 POC，验证 WinUI 3 WebView2 + 指定 UA 是否能稳定取到 `exchange_code`

### R-04 Windows cookie 清理可能影响用户当前会话

Legendary 在 Windows 上先打开 logout URL 再登录，说明嵌入式登录存在会话清理需求。

要求：

- 若本仓库实现类似策略，必须明确提示用户
- 不得静默清理用户已有 Epic 会话

### R-05 当前仓库零运行态证据

到本轮为止，仓库里还没有任何 WebView2 登录 POC 或真实日志，只有对外部实现的静态分析。

要求：

- 正式编码前先做小范围可丢弃验证
- 若 POC 证明拿不到稳定 `exchange_code`，立即止损，不继续扩大契约改造

---

## 5. 建议的实施顺序

### Phase W0：最小 POC

先验证两个问题，不急着改主线：

1. WinUI 3 WebView2 是否能稳定加载当前 Epic 登录页
2. 在合适 User-Agent 下，是否能实际拿到 `exchange_code`

若 W0 失败，则不进入主仓库正式实现。

### Phase W1：Auth completion 契约升级

若 W0 成功：

- 把 completion 输入升级为窄 DTO
- 新增 `ExchangeCodeGrantExecutor`
- 保持现有 `authorization_code` 路线不回退

### Phase W2：Presentation 登录容器

- 增加一个临时对话框或专用登录页承载 WebView2
- 只负责收集 `exchange_code` 并回调 Auth
- 不在页面里堆协议状态机

### Phase W3：失败回退与日志

- WebView2 登录失败时可一键退回现有系统浏览器路径
- 日志按“登录模式 / completion kind / grant / 结果”记录
- 禁止记录原始 `exchange_code`、access token、refresh token

### Phase W4：运行态验收

- 显式构建并运行真实 WinUI 3 宿主
- 验证：
  - WebView2 可正常展示登录页
  - 成功拿到 `exchange_code`
  - `grant_type=exchange_code` 正确完成登录
  - 失败时可回退到当前人工链路

---

## 6. 最终建议

当前阶段建议：

1. 不要把 WebView2 当作“当前登录必须补上的缺口”；它不是底线能力，而是高级交互优化
2. 若近期目标是稳定、最小变更，现有系统浏览器 + 人工兜底已经够用，不必急于实现 WebView2
3. 若近期目标是进一步压缩人工步骤，那么下一步不应直接大改主线，而应先做 `W0` 级别最小 POC，验证 WinUI 3 WebView2 是否真能稳定拿到 `exchange_code`

也就是说：

- 从协议上，WebView2 不是必须
- 从产品体验上，它可能值得做
- 从工程节奏上，它只适合在“先验证可行，再升级契约”的前提下推进
# EGL refresh token 导入预研

> 本文档承接 [13-LegendaryAuthReferenceDesign.md](13-LegendaryAuthReferenceDesign.md) 中的 Phase L3，目标是判断“从 Epic Games Launcher 导入现有登录态”是否适合作为本仓库的下一条 Auth 演进路线。  
> 结论基于两部分事实：
> 1. 对 `legendary-gl/legendary` 的 `auth_import()`、`EPCLFS` 与 `EPCAPI.start_session(refresh_token=...)` 行为分析；
> 2. 本仓库当前 Auth / Shell / Settings / 存储边界与本机可验证运行态证据。

---

## 0. 一页结论

1. 从技术上看，EGL refresh token 导入是可行路线，而且与当前仓库已完成的 Auth Phase L1 结构相容。
2. Legendary 的 `--import` 并不是“复用现成 bearer token”，而是：
   - 读取 `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini`
   - 从 `[RememberMe]` 的 `Data` 字段取出 base64 负载
   - 若负载不是明文 JSON，则尝试解密
   - 从结果里取 `Token`
   - 再调用标准 `grant_type=refresh_token` + `token_type=eg1` 建立新会话
3. 这条路比继续猜 loopback 更贴近 Legendary 的成熟实践，但它有两个硬风险：
   - 可能使 EGL 当前登录态失效，Legendary README 明确提示“会把 Epic Launcher 登出”
   - Legendary 的解密实现位于 GPL 代码库中，不能把它的自定义 AES/常量表直接搬进本仓库
4. 因此，本仓库若要推进，应把它定义为“高级导入入口”，而不是默认登录路径：
   - Shell 只提供明确风险提示和用户确认
   - Auth 内部新增 `ExternalRefreshToken` 结果来源和 `refresh_token` grant 执行链路
   - 读取 EGL RememberMe 的逻辑封装在 Infrastructure.Auth 内部
5. 当前机器上 `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini` 不存在，因此本轮只能完成静态预研，不能完成真实运行态验收。

---

## 1. 外部事实：Legendary 实际做了什么

### 1.1 导入来源

Legendary 的 `auth_import()` 读取的是 EGL 的本地配置文件，而不是网络接口：

- Windows 路径：`%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini`
- 配置节：`[RememberMe]`
- 关键字段：`Data`

Legendary 先把 `Data` 做 base64 解码，然后判断首字节是否是 `{`：

- 如果是 `{`，按明文 JSON 解析
- 如果不是，则视为加密数据并尝试解密

最终它期望在解析出的对象中拿到 `Token` 字段，并把该值当作 refresh token 使用。

### 1.2 导入后的登录建立

Legendary 不把导入的数据直接当“已完成会话”落地，而是立即调用：

- `grant_type=refresh_token`
- `refresh_token=<RememberMe.Token>`
- `token_type=eg1`

换句话说，EGL 导入本质上只是“换一种 refresh token 的来源”，后续仍走正常的 OAuth token endpoint。

### 1.3 用户影响

Legendary README 和 CLI 帮助都把 `--import` 描述为“Import Epic Games Launcher authentication data”，并明确提示：

- 会把 Epic Games Launcher 登出

这说明即使技术链路成立，也必须把它定义为高风险、高语义负担的高级入口，而不能默默在后台执行。

### 1.4 版权与许可证风险

Legendary 仓库许可证是 GPL 系列许可；其 `legendary/utils/egl_crypt.py` 还明确说明是基于外部 AES Python 实现裁剪而来。

对本仓库的直接含义：

- 可以参考“数据来源、字段和流程”
- 不能直接复制 Legendary 的解密实现、常量表或代码片段
- 若需要支持加密 RememberMe 数据，必须用 .NET 标准密码学库写独立实现，并经过单独的许可证/合规审查

---

## 2. 本仓库现状与兼容性

### 2.1 已有的正面条件

当前仓库已经具备以下承接条件：

1. Auth Phase L1 已落地 `EpicLoginResultKind.ExternalRefreshToken`
2. Auth 模块已经按“结果归一化 + grant 执行器”组织，不必再把新的登录来源塞回旧的巨型 Handler 方法
3. `ITokenStore` 已存在，会话建立后可以继续沿用当前保存/恢复/刷新链路
4. Shell 已有 `IDialogService.ShowConfirmAsync(...)`，可以承载风险确认，不需要先做复杂 UI 基建

### 2.2 当前边界限制

本仓库的约束同样很明确：

- 不能把 EGL 配置文件解析逻辑泄漏到 Shell / Settings / App
- 不能把“外部会话导入”做成 Settings 页面里的常驻偏好项
- 不能通过全局静态状态或临时文件绕过现有 `ITokenStore`
- 不能在日志里写出 RememberMe 原始内容、refresh token 或完整配置负载

### 2.3 当前存储实现的现实情况

文档上 `ITokenStore` 写的是“Windows Credential Locker”，但当前实现仍是：

- [src/Launcher.Infrastructure/Auth/FileTokenStore.cs](../../src/Launcher.Infrastructure/Auth/FileTokenStore.cs)

也就是 DPAPI + 本地 `.tokens` 文件。

这不阻塞 EGL 导入预研本身，但意味着：

- 导入后的 refresh token 仍会落到当前 DPAPI 文件存储链路
- 若后续把 EGL 导入正式上线，最好把 TokenStore 文档漂移问题一起收口，而不是继续扩大偏差

### 2.4 本机可验证证据

本轮对当前机器做了只读检查，只输出是否存在，不读取敏感内容：

- `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini`

结果：

- `MISSING`

因此本轮只能完成静态设计预研，不能证明当前机器一定存在可导入的 EGL RememberMe 会话。

---

## 3. 推荐的实现边界

### 3.1 不应放在哪

以下位置都不应承载导入逻辑：

- Settings 页面
- ShellViewModel 中的文件解析/解密代码
- App 宿主层
- Background Worker

原因：这条链路本质上仍是 Auth 的“外部授权结果来源”，不是配置管理，也不是宿主激活能力。

### 3.2 应放在哪

推荐全部收敛在 `Launcher.Infrastructure.Auth`：

| 组件 | 建议职责 |
|------|----------|
| `EpicLauncherRememberMeReader` | 读取 EGL `GameUserSettings.ini`，提取并解析 `RememberMe.Data` |
| `ExternalRefreshTokenGrantExecutor` | 对归一化后的外部 refresh token 执行 `grant_type=refresh_token` |
| `AuthService` | 编排导入动作、保存会话、发布认证成功/失败事件 |
| `ShellViewModel` | 只触发“导入 EGL 登录态”动作并显示确认提示 |

### 3.3 配置建议

默认情况下，Windows 本机导入路径应固定使用系统目录：

- `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows`

若确实需要测试或兼容非标准环境，可考虑只加内部 override：

- `EpicOAuth:EglAppDataPathOverride`

但该 override 只应存在于：

- `appsettings.Local.json`
- 测试配置

明确不建议：

- 放到 Settings UI
- 让终端用户日常手改这个路径

---

## 4. 推荐交互形态

这条路不应取代默认浏览器登录，而应作为一个高语义成本的高级入口：

1. 用户点击“高级：从 Epic Launcher 导入登录态”
2. Shell 弹确认框，明确提示：
   - 需要本机已安装并登录 EGL
   - 可能导致 EGL 当前会话失效
   - 应用不会显示或记录原始 token
3. 用户确认后，Shell 调用新的 Auth 契约入口
4. Auth 内部读取本地配置并尝试建立新会话
5. 成功则进入既有 `SessionAuthenticated` 链路；失败则返回结构化错误给 Shell

第一版不需要：

- 自定义复杂对话框
- 文件夹选择器
- 路径输入框

---

## 5. 风险清单

### R-01 会使 EGL 掉线

这是最高优先级的产品风险。若用户当前正在使用 EGL，本功能可能把其会话踢掉。

要求：

- UI 文案必须前置说明
- 日志中记录“用户已确认该风险”，但不记录任何敏感值

### R-02 RememberMe 数据格式可能漂移

Legendary 的逻辑已经表明数据既可能是明文 JSON，也可能是加密负载。未来 EGL 版本还可能继续变更。

要求：

- 读取器必须区分“文件不存在 / 节不存在 / 字段不存在 / 数据不可解密 / 无 Token 字段 / token exchange 失败”
- 错误必须结构化，不得统一折叠为“导入失败”

### R-03 许可证与实现污染

不应直接复制 Legendary 的 `egl_crypt.py`、常量表或其衍生实现。

要求：

- 若进入实现，必须使用 .NET 自带加密库写独立实现
- 加密分支应有单独的合规检查说明

### R-04 当前机器无法运行态验收

当前机器缺少 EGL 配置文件，导致本轮无法做端到端导入验证。

要求：

- 实现前先准备一台装有并已登录 EGL 的 Windows 机器
- 或构造脱敏测试样本覆盖读取器与解密器单测

---

## 6. 日志与测试要求

### 6.1 推荐日志事件

| 事件 | 必要字段 |
|------|----------|
| `Auth EGL import requested` | `source=shell`, `user_confirmed=true` |
| `Auth EGL config located` | `exists=true/false`, `override=false/true` |
| `Auth EGL remember-me parsed` | `mode=plain_json|encrypted`, `has_token=true/false` |
| `Auth token exchange started` | `grant_type=refresh_token`, `source=egl_import` |
| `Auth token exchange failed` | `grant_type=refresh_token`, `source=egl_import`, `provider_error_code` |
| `Auth token exchange succeeded` | `grant_type=refresh_token`, `source=egl_import` |

### 6.2 禁止记录

- `RememberMe.Data` 原文
- 解码后的 JSON 原文
- refresh token 原文
- access token 原文

### 6.3 最小测试矩阵

1. 配置文件不存在
2. `[RememberMe]` 节不存在
3. `Data` 字段不存在
4. base64 负载是明文 JSON 且含 `Token`
5. base64 负载为加密内容且成功解密
6. 解密失败
7. `grant_type=refresh_token` 请求参数正确
8. 导入成功后进入既有会话保存与恢复链路

---

## 7. 建议的实施顺序

### Phase E0：合规与样本准备

- 确认是否允许实现 EGL RememberMe 解密分支
- 准备不含真实账号信息的测试样本
- 明确是否需要支持“加密 RememberMe”还是先只支持明文 JSON

### Phase E1：Auth 内部读取器

- 新增 `EpicLauncherRememberMeReader`
- 只做读取、解析、错误分类
- 不碰 Shell UI

### Phase E2：refresh_token grant 执行器

- 新增 `ExternalRefreshTokenGrantExecutor`
- 接入当前 Phase L1 的 grant executor 体系

### Phase E3：Shell 高级入口

- 增加一个明确带风险提示的导入动作
- 只做确认与调用，不做协议处理

### Phase E4：运行态验收

- 在存在 EGL 登录态的机器上验证
- 重点确认：
  - 成功导入
  - EGL 是否会掉线
  - 导入后重启恢复是否正常

---

## 8. 最终建议

当前阶段建议：

1. 可以继续推进 EGL refresh token 导入，但它应被定义为“高级导入路径”，不是默认主登录链路
2. 真正开始写代码前，先单独确认“是否允许实现加密 RememberMe 解密分支”
3. 若暂时不想处理许可证/解密风险，则下一步更适合优先预研 WebView2 exchange code

也就是说：

- 从工程结构上，EGL 导入是可接的
- 从产品和合规角度，它比 WebView2 更敏感，进入实现前要多一个风险闸门

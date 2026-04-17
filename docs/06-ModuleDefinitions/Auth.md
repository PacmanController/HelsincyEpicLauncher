# Auth 模块

---

## 架构定义

### 职责

- Epic Games OAuth 2.0 登录流程
- Access Token / Refresh Token 管理
- Token 自动刷新（过期前主动刷新）
- 会话缓存与恢复（启动时自动恢复）
- 登出和会话清理
- 安全存储（Windows Credential Locker）

### 不负责

- UI 登录页面布局（由 Presentation 处理）
- 用户偏好设置（由 Settings 模块处理）
- 网络请求重试策略（由 Infrastructure 的 HTTP 层统一处理）

### 依赖

| 依赖目标 | 用途 |
|---------|------|
| `ITokenStore`（Infrastructure） | 安全存储 Token |
| `EpicAuthClient`（Infrastructure） | 调用 Epic OAuth API |
| `Launcher.Shared` | Result 模型 |

### OAuth 回调约束

- 当前止血版本仍使用系统浏览器打开 Epic 登录页，但默认路径不再要求用户回填完整 JSON 响应；若需要人工继续登录，只允许输入 `authorizationCode` 或完整回调 URL
- App 宿主现在已能把启动参数或第二实例转发过来的“完整回调 URL 候选负载”自动交回 Auth；因此一旦后续拿到可用的 loopback 或协议回调来源，应用内部已具备自动完成登录的消费骨架
- 当前 clientId 的成熟外部交互链路不是本地 loopback localhost 回调，因此登录契约不再要求 UI 直接等待本地 HTTP 回调
- 若后续需要支持其他类型的回调接收方式，必须封装在 Auth 模块内部，不能把协议细节泄漏到 Shell / Settings / App
- 回调或授权结果处理必须校验输入有效性，并在 provider 返回 `error` / `error_description` 或 token 交换 `invalid_grant` 时把失败原因准确透传回应用日志

### 谁可以依赖 Auth

| 模块 | 用途 |
|------|------|
| Shell | 显示登录状态、用户头像 |
| FabLibrary | 获取 Access Token 调用 Fab API |
| Downloads | 获取 Access Token 下载认证资源 |
| EngineVersions | 获取 Access Token 访问引擎列表 |

---

## API 定义

> 详见 [05-CoreInterfaces.md](../05-CoreInterfaces.md) 第 4 节 `IAuthService`

### 补充：Token 刷新策略

```csharp
/// <summary>
/// Token 存储接口。由 Infrastructure 层实现。
/// </summary>
public interface ITokenStore
{
    Task SaveTokensAsync(TokenPair tokens, CancellationToken ct);
    Task<TokenPair?> LoadTokensAsync(CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}

public sealed class TokenPair
{
    public string AccessToken { get; init; } = default!;
    public string RefreshToken { get; init; } = default!;
    public DateTime ExpiresAt { get; init; }
}
```

---

## 关键流程

### 首次登录

```
1. 用户点击"登录"按钮
2. ShellViewModel → IAuthService.StartAuthorizationCodeLoginAsync()
3. AuthService 打开与当前 clientId 兼容的 Epic 登录页面
4. 用户在浏览器中完成登录；若宿主收到外部回调 URL，则会自动转交 Auth 尝试完成登录；若当前环境尚未接通自动回调，则通过显式“继续登录”入口提交 `authorizationCode` 或完整回调 URL
5. ShellViewModel 收集用户输入 → IAuthService.CompleteAuthorizationCodeLoginAsync(rawInput)
6. AuthService 从输入中提取 authorization code，并换取 access_token + refresh_token；若 provider 返回 `invalid_grant`，则提示用户重新获取新的授权码
7. TokenStore 安全存储 token pair
8. 返回 AuthUserInfo 给 Shell
9. Shell 更新登录状态 UI
```

### 启动时会话恢复

```
1. App 启动 Phase 2
2. 调用 IAuthService.TryRestoreSessionAsync()
3. 从 TokenStore 加载缓存的 token pair
4. 检查 access_token 是否过期
   a. 未过期 → 直接使用
   b. 已过期 → 用 refresh_token 刷新
   c. refresh_token 也过期 → 返回失败，需要重新登录
5. 恢复成功 → 更新 Shell 登录状态
6. 恢复失败 → Shell 显示"需要登录"状态
```

### Token 自动刷新

```
1. 任何模块调用 IAuthService.GetAccessTokenAsync()
2. AuthService 检查当前 access_token：
   a. 有效期 > 5 分钟 → 直接返回
   b. 有效期 < 5 分钟 → 主动刷新
   c. 已过期 → 刷新
3. 刷新成功 → 更新 TokenStore，返回新 token
4. 刷新失败 → 发布 SessionExpiredEvent
5. Shell 收到事件 → 提示用户重新登录
```

### 登出

```
1. 用户点击"登出"
2. IAuthService.LogoutAsync()
3. 调用 Epic API 撤销 token（如支持）
4. 清除 TokenStore
5. 清除内存中的会话信息
6. 发布 SessionExpiredEvent
7. Shell 导航到登录界面
```

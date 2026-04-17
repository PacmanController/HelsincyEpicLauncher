# Auth Loopback Redirect Inquiry Template

> 用途：向 Epic 或其他外部维护方确认当前 OAuth client 是否支持桌面应用的 loopback 自动回调。  
> 目标：拿到**精确可用**的 `redirect_uri`、浏览器交互入口和 token exchange 参数要求，而不是泛泛的“支持 OAuth code flow”。

---

## 发送前先准备的事实

在发出询问前，先把下面这些信息准备好，避免来回补充：

1. 当前 clientId：`34a02cf8f4414e29b15921876da36f9a`
2. 当前客户端形态：Windows 桌面应用（WinUI 3 / Windows App SDK），当前宿主为 unpackaged app
3. 当前尝试使用的 redirect URI：`http://localhost:6780/callback`
4. 当前浏览器错误现象：Epic 登录页直接提示“redirect URL is not valid for this client”
5. 当前应用端能力约束：
   - 只能稳定接收**精确固定**的 loopback URI
   - 当前实现要求 `http` + loopback + 显式端口
   - 当前实现会校验 `state` 和 callback path
   - 自动回调流 token exchange 目前会带回 `redirect_uri`
6. 如有可能，附上：
   - 浏览器错误页截图
   - 当前授权 URL（脱敏后）
   - 当前日志中的失败摘要（脱敏后）

---

## 推荐外发正文（英文）

```text
Subject: Confirming supported redirect URI for Epic OAuth desktop login client 34a02cf8f4414e29b15921876da36f9a

Hello,

We are integrating Epic OAuth login into a Windows desktop application and need to confirm the exact browser callback configuration supported by clientId `34a02cf8f4414e29b15921876da36f9a`.

Our current implementation uses a fixed loopback callback and the browser is opened for interactive login. We currently attempt to use:

`http://localhost:6780/callback`

However, the Epic login page rejects this with a redirect URL / redirect URI invalid-for-client error before the authorization code callback completes.

To avoid implementing against the wrong assumptions, could you please confirm the following exact details for this client:

1. Does this client support any browser-based loopback redirect URI for desktop/native login flows?
2. If yes, what is the exact allowed redirect URI (full value), including:
   - scheme (`http` or `https`)
   - host (`localhost`, `127.0.0.1`, or another loopback host)
   - required port
   - required callback path
3. For interactive browser login with this client, which authorize entry point is the correct one?
   - `https://www.epicgames.com/id/authorize?...redirect_uri=...`
   - or `https://www.epicgames.com/id/login?redirectUrl=...`
4. After receiving the authorization code, should the token exchange request include the same `redirect_uri` again?
5. Is `token_type=eg1` required for authorization-code exchange for this client, or only for specific flows?
6. Is standard client authentication with `client_id:client_secret` (Basic Auth) expected for the token request in this flow?
7. What is the expected callback query shape on success and on failure? For example:
   - success: `code`, `state`
   - failure: `error`, `error_description`
8. If this client does not support loopback redirect URIs at all, is there another approved client for desktop/native applications with equivalent access/scopes that does?

We are not looking for wildcard behavior. We only need the exact supported redirect URI and parameter expectations so we can align the desktop client implementation correctly.

Thanks.
```

---

## 可直接追问的补充问题

如果对方回复仍然模糊，再继续追问这些点：

1. “请直接给出 allowlist 中可用的完整 redirect URI 字符串，而不是仅说明支持 localhost。”
2. “请明确 token exchange 是否必须回传 `redirect_uri`，以及是否必须携带 `token_type=eg1`。”
3. “若当前 clientId 不支持 loopback，请明确这是该 client 的固定限制，还是仅当前 redirect 值不正确。”
4. “若推荐的浏览器交互入口是 `id/login?redirectUrl=...`，请说明 redirectUrl 最终应指向什么格式的回调 URI。”

---

## 收到回复后的判定规则

### 可以直接进入 loopback 实施

满足以下全部条件时，才可以直接进入代码接入：

1. 对方给出了**精确完整**的 `redirect_uri`
2. 该 URI 与当前宿主能力兼容：
   - loopback
   - 有显式端口
   - path 可固定
3. authorize 入口形式明确
4. token exchange 参数矩阵明确

### 不能直接实施，必须继续预研

出现以下任一情况时，不要直接开写正式自动回调：

1. 只得到“支持 localhost”但没有精确 URI
2. 只得到网页登录入口，没有 token exchange 参数要求
3. 对方确认当前 clientId 不支持 loopback
4. 回复要求使用当前宿主尚不具备的回调方式（例如应用协议，但未说明如何 allowlist）

---

## 当前仓库侧已知前提

便于后续执行时快速对照：

1. 当前宿主已具备“自动消费外部回调 URL 候选负载”的内部骨架
2. 当前 Auth 模块已具备：
   - authorization code / callback URL 解析
   - token exchange
   - session save / restore
3. 当前真正缺的不是应用内消费能力，而是**外部可用的真实 redirect 方案**
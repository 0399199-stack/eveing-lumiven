# Web 控制台访问 Token 配置说明

## 概述

为了增强完整版 Web 控制台的安全性，系统支持通过访问令牌（Token）来保护控制台的访问。只有提供正确令牌的用户才能访问完整版控制台。

## 功能特性

- ✅ 支持通过环境变量配置访问令牌
- ✅ 支持通过数据库配置访问令牌
- ✅ 提供 Token 验证 API
- ✅ 自动登录页面（当需要 token 但未提供时）
- ✅ Cookie 自动保存 token（便于后续访问）
- ✅ 简单版控制台（`/console`）无需 token
- ✅ 完整版控制台（`/console/full`）需要 token（如果配置了）

## 配置方式

### 方式 1：环境变量（推荐）

在启动程序前设置环境变量：

**Windows (PowerShell):**
```powershell
$env:WX_CHANNEL_WEB_CONSOLE_TOKEN = "your-secret-token-here"
.\wx_channel.exe
```

**Windows (CMD):**
```cmd
set WX_CHANNEL_WEB_CONSOLE_TOKEN=your-secret-token-here
wx_channel.exe
```

**Linux/macOS:**
```bash
export WX_CHANNEL_WEB_CONSOLE_TOKEN=your-secret-token-here
./wx_channel
```

### 方式 2：数据库配置

如果使用数据库存储配置，可以通过数据库设置 `web_console_token` 字段。

## 访问方式

### 简单版控制台（无需 Token）

访问地址：`http://127.0.0.1:2025/console`

简单版控制台无需 token，可以直接访问。

### 完整版控制台（需要 Token）

访问地址：`http://127.0.0.1:2025/console/full`

#### 情况 1：未配置 Token

如果未配置 `WX_CHANNEL_WEB_CONSOLE_TOKEN`，完整版控制台可以直接访问，无需验证。

#### 情况 2：已配置 Token

如果配置了 token，访问 `/console/full` 时：

1. **首次访问**：如果没有提供 token，会显示登录页面，要求输入 token
2. **提供 Token**：
   - 通过 URL 参数：`/console/full?token=your-token`
   - 通过 Cookie：如果之前保存过 token，会自动使用
   - 通过登录页面：在登录页面输入 token 后点击"验证并访问"

3. **Token 验证通过**：自动保存到 Cookie，并显示完整版控制台
4. **Token 验证失败**：显示错误提示，要求重新输入

## API 接口

### 验证 Token

**端点：** `POST /api/console/verify-token`

**请求体：**
```json
{
  "token": "your-token-here"
}
```

**响应（成功）：**
```json
{
  "success": true,
  "data": {
    "valid": true,
    "message": "token verified"
  }
}
```

**响应（失败）：**
```json
{
  "success": false,
  "error": "invalid token"
}
```

**响应（未配置 token）：**
```json
{
  "success": true,
  "data": {
    "valid": true,
    "message": "token not required"
  }
}
```

## 使用示例

### 示例 1：通过环境变量配置

```bash
# 设置 token
export WX_CHANNEL_WEB_CONSOLE_TOKEN=my-secret-token-123

# 启动程序
./wx_channel

# 访问完整版控制台
# 浏览器打开：http://127.0.0.1:2025/console/full
# 在登录页面输入：my-secret-token-123
```

### 示例 2：通过 URL 参数访问

```
http://127.0.0.1:2025/console/full?token=my-secret-token-123
```

### 示例 3：使用 API 验证 Token

```javascript
fetch('/api/console/verify-token', {
    method: 'POST',
    headers: {
        'Content-Type': 'application/json'
    },
    body: JSON.stringify({
        token: 'my-secret-token-123'
    })
})
.then(res => res.json())
.then(data => {
    if (data.success && data.data.valid) {
        console.log('Token 验证成功');
    } else {
        console.log('Token 验证失败');
    }
});
```

## 安全建议

1. **使用强密码**：Token 应该足够复杂，建议使用随机生成的字符串
2. **定期更换**：定期更换 token 以提高安全性
3. **不要分享**：不要将 token 分享给他人
4. **本地使用**：控制台设计为本地使用，不建议部署到公网
5. **HTTPS**：如果必须部署到公网，建议使用 HTTPS

## 生成随机 Token

### 使用 OpenSSL（Linux/macOS）

```bash
openssl rand -hex 32
```

### 使用 PowerShell（Windows）

```powershell
-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 32 | ForEach-Object {[char]$_})
```

### 使用 Node.js

```javascript
require('crypto').randomBytes(32).toString('hex')
```

### 使用 Python

```python
import secrets
secrets.token_hex(32)
```

## 常见问题

### Q: 如何禁用 Token 验证？

A: 不设置 `WX_CHANNEL_WEB_CONSOLE_TOKEN` 环境变量，或从数据库中删除 `web_console_token` 配置。

### Q: Token 验证失败怎么办？

A: 检查：
1. Token 是否正确（注意大小写和特殊字符）
2. 环境变量是否已正确设置
3. 程序是否已重启（环境变量更改后需要重启）

### Q: 可以同时使用简单版和完整版控制台吗？

A: 可以。简单版控制台（`/console`）始终可以访问，完整版控制台（`/console/full`）需要 token（如果配置了）。

### Q: Cookie 中的 token 会过期吗？

A: 不会自动过期，但可以通过清除浏览器 Cookie 来清除 token。

### Q: 如何清除已保存的 token？

A: 在浏览器中清除 Cookie，或使用浏览器的开发者工具删除 `web_console_token` Cookie。
## 技术实现

- **配置加载优先级**：数据库配置 > 环境变量 > 默认值（空）
- **Token 存储**：服务端不存储 token，仅用于验证
- **Cookie 安全**：Token 存储在 Cookie 中，仅用于后续访问验证
- **验证流程**：服务端在提供完整版控制台前验证 token，前端可选验证

## 相关文件

- 配置文件：`internal/config/config.go`
- API 处理器：`internal/handlers/console_api.go`
- 路由处理：`main.go`
- 前端页面：`web/console.html`

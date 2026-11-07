# 把 MCP 层交给别的 Harness 用（Claude Code / CodeBuddy / WorkBuddy / Cursor…）

> 结论：**可以，而且不需要改 MCP 层一行代码。**
> MCP（Model Context Protocol）是宿主无关的开放协议；我们的 `Demo.MCP` 只是「工具实现」，
> 哪个宿主来连、用什么模型，与工具无关。本仓库提供两种传输方式：
>
> | 传输 | 命令 | 适合 |
> | --- | --- | --- |
> | **stdio**（本地子进程） | `Demo.Host.dll --stdio` | 绝大多数桌面/CLI harness（Claude Desktop、Claude Code、CodeBuddy、WorkBuddy、Cursor、VS Code Copilot…） |
> | **Streamable HTTP** | `Demo.Host.dll --http`（`http://127.0.0.1:5199`） | 只支持 URL 接入的远端/网页版 harness |

两种模式都用同一套 `[McpServerToolType]` 工具（当前 8 个：订单查询/新增/修改/改状态/取消/删除）。

---

## 一、先拿到可执行的入口（不要用 `dotnet run` 直接接 stdio）

stdio 模式下 **stdout 是协议通道**，任何构建输出/日志混进去都会让握手失败。所以：

```powershell
# 推荐：发布成可执行文件
dotnet publish src/Demo.Host/Demo.Host.csproj -c Release -o "$PWD\publish"
# 入口：<仓库>\publish\Demo.Host.exe
```

或者直接用已构建产物：`<仓库>\src\Demo.Host\bin\Debug\net10.0-windows\Demo.Host.dll`（配合 `dotnet` 命令）。

> 日志已经全部走 stderr（stdio 模式里 `LogToStandardErrorThreshold = Trace`），不会污染协议。

---

## 二、stdio 接入配置

### 2.1 通用模板（大多数 harness 的 `mcp.json` / settings 结构）

```json
{
  "mcpServers": {
    "karl-ai-orders": {
      "type": "stdio",
      "command": "C:\\Codes\\Karl.AiExtension\\publish\\Demo.Host.exe",
      "args": ["--stdio"]
    }
  }
}
```

如果 harness 不支持 `type` 字段，删掉它即可（默认就是 stdio）。
若用 `dotnet` 启动 dll，则：

```json
{
  "mcpServers": {
    "karl-ai-orders": {
      "command": "dotnet",
      "args": ["C:\\Codes\\Karl.AiExtension\\src\\Demo.Host\\bin\\Debug\\net10.0-windows\\Demo.Host.dll", "--stdio"]
    }
  }
}
```

### 2.2 各 harness 放哪

| Harness | 配置位置 / 入口 | 备注 |
| --- | --- | --- |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` | 键名 `mcpServers` |
| Claude Code | 项目 `.mcp.json`，或 `claude mcp add karl-ai-orders -- <exe> --stdio` | 键名 `mcpServers` |
| VS Code Copilot | `.vscode/mcp.json` | 键名 `servers`（注意不是 `mcpServers`） |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` | 键名 `mcpServers` |
| CodeBuddy / WorkBuddy 等 | 设置里的「MCP Server / 扩展工具」页，或产品文档指定的 `mcp.json` | 认准两点：**stdio 类型 + command/args**；字段名以各自文档为准 |

> WorkBuddy / CodeBuddy 这类产品通常提供「添加 MCP Server」的可视化入口：
> 选 **本地命令（stdio）**，命令填 `Demo.Host.exe` 的绝对路径，参数填 `--stdio` 即可。
> 如果只支持填 URL，就用下面的 HTTP 模式。

### 2.3 验证是否连上

让 harness 列出工具，应看到 8 个 `order_*`；或让它执行：

- 「统计一下订单情况」→ 调 `order_summary`
- 「新增一个客户叫测试客户，金额 100」→ 调 `order_create`
- 「删除 SO-1006」→ 调 `order_delete`（会要求 `confirm=true`）

---

## 三、Streamable HTTP 接入配置

### 方式 A（推荐）：WPF 应用内开关，同进程暴露

打开 WPF 应用 → 右侧「**工具与契约**」页签 →「**对外暴露 MCP**」：

1. 填端口（默认 `5199`）；
2. 点「开启暴露」——同进程内启动只监听 `127.0.0.1` 的 MCP HTTP 端点；
3. 把页面显示的 URL 填到 harness（默认 `http://127.0.0.1:5199`）。

```json
{
  "mcpServers": {
    "karl-ai-orders": {
      "type": "http",
      "url": "http://127.0.0.1:5199"
    }
  }
}
```

- 与 stdio 不同：**这是同一个进程**，harness 调工具看到的就是 WPF 应用里的实时数据（同一套单例/仓储）；
- 默认关闭，随时「关闭暴露」即停止监听；可改端口后重开；
- 采用无状态模式（`Stateless = true`），只监听 `127.0.0.1`，没有鉴权。要对外暴露请加反向代理/认证，并评估工具风险。

### 方式 B：无界面（不开 WPF 应用时）

```powershell
dotnet run --project src/Demo.Host -- --http
# MCP Streamable HTTP Server: http://127.0.0.1:5199
```

适合 CI、后台服务或只想跑 MCP、不打开桌面端的场景；数据是 Demo.Host 进程自己的（不会与 WPF 应用共享）。

---

## 四、能被 harness 用，但要注意的边界

1. **数据边界看接入方式**：
   - **WPF 应用内开关（方式 A）**：同进程，和界面共用同一套单例/仓储，harness 看到的是实时数据；
   - **stdio / Demo.Host --http（方式 B）**：独立进程，只看到自己进程里的数据（Demo 用的是内存仓储）。
   生产接入时把仓储换成真实数据库/API（`IOrderRepository` 已是抽象），或让 MCP 层调用你应用的服务端接口。
2. **工具语义不变，模型换了**：`[McpServerTool]` 的名字/描述决定模型怎么用；不同 harness 的模型能力不同，
   建议工具参数保持扁平、数量控制（单域 5~8 个），并在描述里写清前置条件。
3. **危险操作**：`order_delete` 要求 `confirm=true`（封装层拦截）。跨 harness 使用时建议再加宿主侧审批/白名单，
   或在封装层接「人机确认」。
4. **版本/路径**：harness 配置里用**绝对路径**；升级后重新 publish 并保持路径稳定。
5. **Windows 控制台窗口**：用 exe 启动 stdio 时，若是 WinExe 不会弹窗；`Demo.Host` 是控制台 exe，
   部分 harness 会隐藏子进程窗口（无副作用）。

---

## 五、实测记录（本仓库）

- **WPF 应用内开关（方式 A）**：页面上「开启暴露」→ SDK `HttpClientTransport` 连接 `http://127.0.0.1:5199` →
  `TOOLS(8)`、`order_summary`、`order_create` 均成功，且数据与界面同一份（统计总数随界面变化）；
  「关闭暴露」后连接被拒绝，再次开启可复用；
- **stdio**：用独立 MCP 客户端子进程连接 → `TOOLS(8)`、`order_summary` 正常返回、非法参数返回可读错误；
- **Demo.Host --http（方式 B）**：`TOOLS(8)`、`order_summary`、`order_create` 均成功；
- 三种模式共用同一套工具代码与同一份契约，不需要为 harness 单独适配。

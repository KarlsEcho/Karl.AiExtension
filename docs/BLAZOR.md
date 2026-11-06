# Blazor 分支说明

Blazor 分支的目标不是「另写一套 AI 界面」，而是验证架构里最重要的一条结论：

> **契约 / 会话核心 / MCP 工具闭环与具体 UI 技术无关。**
> AI.Extension.Core + Demo.Core + 源生成器产出的桥接层可以被 WPF、Blazor、控制台宿主原样复用，
> 各宿主只需要提供自己的「UI 外壳」（渲染、线程调度、启动流程）。

相关项目：

| 项目 | 作用 |
| --- | --- |
| `src/AI.Extension.Blazor` | 可复用 Blazor 外壳：`ChatPanel` / `ToolCallTraceList` / `AddAiExtensionBlazor` / 样式 |
| `src/Demo.BlazorHost` | Blazor Server 演示宿主：订单表格 + AI 聊天面板 + MCP 工具轨迹，固定端口 `http://localhost:5180` |

## 一、复用了什么

Blazor 外壳**没有**任何自己的 MCP 客户端、工具循环或业务逻辑，全部复用已有分层：

| 能力 | 复用来源 |
| --- | --- |
| 对话 + 工具循环 + 历史 + 错误回灌 | `AI.Extension.Core` 的 `ChatSession.SendAsync(string) → AiChatReply{Text, ToolCalls}` |
| 进程内 MCP Server ⇄ Client 桥 | `AI.Extension.Core/Mcp/McpServerBridge`（`ConnectAsync()` 后 `Tools` 可用） |
| 业务数据源 / 契约实现 | `Demo.ViewModel.OrdersViewModel`（`ObservableCollection<Order>`、`StatusMessage`、`SelectedOrder`，同时实现 `IOrderService`） |
| MCP 工具（语义封装） | `Demo.MCP.OrderTools`（`order_search` / `order_get` / `order_cancel` / `order_summary`） |
| 组合根 | `Demo.Core.DemoBootstrap.AddDemoCore()`（业务 + AI 会话 + 进程内 MCP Server） |
| UI 上下文日志 | `IUiActivityTracker`（Blazor 与 WPF 写的是同一份环形日志） |
| 线程调度抽象 | `IUiDispatcher`，Blazor 下继续使用默认 `ImmediateUiDispatcher` |

## 二、组件与注册入口

### `ChatPanel`（`src/AI.Extension.Blazor/ChatPanel.razor`）

- 通过 `[Inject] ChatSession Session` 发消息，调用 `Session.SendAsync(text)`；
- 展示：用户消息、AI 回复、「正在处理」状态、每轮对话的工具调用轨迹（工具名 / 成功与否 / 耗时 / 参数 / 结果预览）；
- 订阅 `ChatSession.ToolCallCompleted` 实时追加轨迹（本轮工具执行过程中就能看到）；
- `Dispose` 时取消订阅，避免单例会话持有已销毁组件；
- 样式：`wwwroot/css/ai-chat-panel.css`，消费方在宿主页面引入
  `_content/AI.Extension.Blazor/css/ai-chat-panel.css`。

`ToolCallTraceList` 是独立的小组件，只接收 `IReadOnlyList<AiToolCallTrace>`，
因此 WPF 之外的其他 Blazor 宿主（Server / WebAssembly 上的纯展示）都能直接复用。

### `AddAiExtensionBlazor(this IServiceCollection)`

```csharp
builder.Services.AddDemoCore();          // 业务 + AI 会话 + MCP Server（组合根）
builder.Services.AddAiExtensionBlazor(); // Blazor 外壳的 UI 服务（幂等 TryAdd）
```

它只做外壳级注册（`AddAiExtensionCore()`：`IUiActivityTracker` + `IUiDispatcher`，均为 `TryAdd`），
**不注册 `ChatSession`**：会话与模型客户端属于组合根职责，由 `AddDemoCore()` /
`AddAiChatSession()` 提供。这样 Blazor 与 WPF/控制台宿主共享同一份 AI 配置与同一个进程内 MCP Server，
而不是各自 new 一套。

## 三、宿主启动流程（`src/Demo.BlazorHost/Program.cs`）

```text
builder.Services.AddDemoCore()                 // 业务 + ChatSession + 进程内 MCP Server
builder.Services.AddAiExtensionBlazor()        // Blazor 外壳 UI 服务
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
builder.Build()
app.UseStaticFiles() / UseAntiforgery() / MapRazorComponents<App>()...
await app.StartAsync()                         // MCP Server 作为 IHostedService 随宿主启动
await app.Services.GetRequiredService<McpServerBridge>().ConnectAsync()   // 拉取工具清单
await app.WaitForShutdownAsync()               // 等价于 Run() 的「保持进程存活」
```

### 为什么最后不是字面上的 `app.Run()`

`app.Run()` 内部会再次调用 `host.StartAsync()`。在已经执行过 `await app.StartAsync()`
的情况下，Kestrel 会抛：

```text
System.InvalidOperationException: Server has already started.
```

（本次实现已实测复现。）因此「StartAsync → ConnectAsync → 保持运行」三步中的最后一步
用 `WaitForShutdownAsync()` 表达，语义与 `Run()` 一致：宿主继续监听，直到 Ctrl+C / SIGTERM。
MCP 连接仍然严格发生在 `StartAsync` 之后、`Run` 之前，`ChatSession` 拿到的 `bridge.Tools` 已就绪。

## 四、与 WPF 分支的差异（重点：线程调度 / SynchronizationContext）

| 维度 | WPF | Blazor（本分支） |
| --- | --- | --- |
| UI 线程模型 | STA + `Dispatcher`，有 UI 线程亲和性；由 `AI.Extension.WPF` 的 `WpfUiDispatcher` 实现 `IUiDispatcher`（`Dispatcher.InvokeAsync(...).Task.Unwrap()`） | 没有 STA UI 线程；Blazor Server 用 circuit 的 `SynchronizationContext`/`Dispatcher` 驱动渲染；默认 `ImmediateUiDispatcher` 足够 |
| 工具调用回写线程 | AI 工具在后台线程完成，必须 `Dispatcher.Invoke` 回 UI 线程 | `ChatSession.SendAsync` 内部大量 `ConfigureAwait(false)`，`ToolCallCompleted` 也在后台线程触发；组件用 **`ComponentBase.InvokeAsync(StateHasChanged)`** 把状态更新与重渲染封送回渲染上下文 |
| 数据绑定 | `ObservableCollection` + `INotifyPropertyChanged` 由 WPF 绑定引擎自动刷新 | 绑定引擎不存在：页面订阅 `OrdersViewModel.PropertyChanged`，回调里 `InvokeAsync(StateHasChanged)` 手动重渲染（`ObservableCollection` 自身的变化不会自动通知 Blazor） |
| 生命周期 | 应用级单例 + 窗口/视图模型 | Blazor Server 每个浏览器 circuit 一个作用域；`ChatSession`/`OrdersViewModel` 是单例（见「已知限制」） |
| 渲染 | 立即渲染 | 默认预渲染（prerender）：页面先以静态 HTML 返回，再建立 circuit 重渲染；`ChatPanel` 在预渲染实例与交互实例中各 `OnInitialized` 一次，靠 `Dispose` 成对取消事件订阅 |
| 启动 | `App.xaml` 启动 → 建容器 → 启动 MCP | `Program.cs` 显式 `StartAsync` → `ConnectAsync` → `WaitForShutdownAsync` |

## 五、已知限制

1. **单例 `ChatSession` 被多个 circuit 共享**：对话历史是进程级共享的，两个浏览器标签会看到同一份历史；
   同一时刻的并发发送由 `ChatSession` 内部的 `SemaphoreSlim` 串行化。若要支持多用户/多会话，
   应把 `ChatSession` 改为 Scoped 并让 `McpServerBridge` 按会话隔离（属于核心层改造，不在 Blazor 外壳范围内）。
2. **非流式输出**：`ChatSession.SendAsync` 只返回最终文本，`ChatPanel` 因此没有 token 级流式打字效果；
   工具轨迹是实时事件，能实时看到。
3. **聊天记录仅在组件内存**：刷新页面后面板清空（服务端 `ChatSession` 历史仍在）；
   `ChatPanel` 没有做持久化。
4. **仅 Interactive Server**：依赖进程内内存管道的 `McpServerBridge`，不能直接跑在 WebAssembly；
   若做 WASM 需要换 HTTP/SSE 版 MCP 客户端（`ModelContextProtocol.AspNetCore`）。
5. **无鉴权/多租户**：演示宿主没有用户身份、没有按用户隔离工具权限；生产化需要补认证与授权。
6. **Demo.Core 依赖 WPF**：`Demo.Core` 是 `net10.0-windows` 且 `UseWPF=true`，所以 `Demo.BlazorHost` 也必须是
   `net10.0-windows`，并在 Windows + WindowsDesktop 运行时上运行。这是现有 Demo 分层带来的耦合，
   不是 Blazor 外壳本身的要求（`AI.Extension.Blazor` 自身是纯 `net10.0`）。
7. **WPF 分支已并行实现**：`AI.Extension.WPF` 提供 `WpfUiDispatcher`、`AddAiExtensionWpf()`、聊天面板与「工具与契约」诊断面板，
   `Demo.View` / `Demo.App` 已完成 WPF 宿主接线（WPF 入口走 `AddDemoCoreForWpf()`）。
   Blazor 分支与它复用同一套 `AI.Extension.Core` + `Demo.Core`，两者互不依赖。

## 六、验证方法

```powershell
# 1) 构建
dotnet build src/Demo.BlazorHost/Demo.BlazorHost.csproj

# 2) 启动（固定 http://localhost:5180，Development 环境由 launchSettings.json 提供）
dotnet run --project src/Demo.BlazorHost

# 3) 另开一个终端验证
$r = Invoke-WebRequest http://localhost:5180 -UseBasicParsing
$r.StatusCode               # 200
$r.Content.Contains('SO-1002')   # True：订单表格已在预渲染 HTML 中
```

验证要点：

- 首页 HTML 含订单号（SO-1001 ~ SO-1005）、`ai-chat-panel`、工具轨迹区域、`order_*` 工具名；
- `/_framework/blazor.web.js` 与 `_content/AI.Extension.Blazor/css/ai-chat-panel.css` 均 200，说明 circuit 与样式可加载；
- 页面交互后可以试：`帮我统计一下现在的订单情况`、`看看 SO-1002 这个订单`、`取消 SO-1001，原因是客户重复下单`，
  工具轨迹会实时出现，取消订单后左侧表格会随 `PropertyChanged` 自动刷新。

## 七、排障记录

- **`dotnet run` 启动即抛 `JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only`**：
  这是仓库里 `bin/` 残留了旧版 `Demo.MCP.dll`（旧版本未给工具序列化配置 `TypeInfoResolver`）导致的；
  源码 `src/Demo.MCP/DemoMcpExtensions.cs` 已经显式配置 `DefaultJsonTypeInfoResolver`。
  清除 `src/Demo.MCP/bin`、`src/Demo.BlazorHost/bin|obj` 后重新构建即可。
- **`app.Run()` 在 `StartAsync` 之后不可用**：见上文「为什么最后不是字面上的 app.Run()」。
- **端口被占用**：先 `Get-NetTCPConnection -LocalPort 5180 -State Listen` 确认，再结束残留进程。

# 架构说明

本文记录这套骨架的依赖关系、运行时调用链、源生成器规则与线程模型，以及和最初设计对话的对应关系。

---

## 1. 分层与依赖

```
┌──────────────────────────────────────────────────────────────────────┐
│ 宿主层                                                               │
│  Demo.App (WPF 入口)          Demo.Host (控制台 / stdio MCP Server)   │
└───────────────┬──────────────────────────────┬───────────────────────┘
                │                              │
┌───────────────▼──────────────────────────────▼───────────────────────┐
│ 组合根（Composition Root）                                           │
│  Demo.Core：AddDemoCore() / AddDemoCoreForWpf() / AddDemoStdioServer()│
└───┬───────────┬──────────────┬───────────────┬───────────────────────┘
    │           │              │               │
    │           │              │               └──► AI.Extension.WPF ──┐
    │           │              │                                      │
    │           │              └──► Demo.MCP ──► Demo.Contract ───────┤
    │           │                        │                            │
    │           │                        └──► AI.Extension.Abstractions
    │           │                                                     │
    │           ├──► Demo.ViewModel ──► Demo.Contract                  │
    │           │        │                                            │
    │           │        └──► AI.Extension.Core ◄─────────────────────┘
    │           │                  │
    │           └──► Demo.Model    └──► AI.Extension.Abstractions
    │
    └──► Demo.View（WPF 视图，只依赖 ViewModel 与 AI.Extension.WPF）
```

要点：

- **UI 框架只在组合根与外壳层出现**：`AI.Extension.Core` 不引用 WPF，因此 Blazor 能复用同一套会话与工具循环。
- **`Demo.MCP` 不引用 `Demo.ViewModel`**：它只依赖契约（编译期，`PrivateAssets=all`）与 `AI.Extension.Abstractions`，通过 DI 拿到的 `IOrderService` 实现（实际是 ViewModel）。
- **生成器只被 `Demo.MCP` 引用为 Analyzer**：产物落在 `Demo.MCP` 程序集。

---

## 2. 运行时调用链

### 2.1 人类路径（UI）

```
Button.Click → AsyncRelayCommand → OrdersViewModel.CancelAsync()
  → IOrderRepository.TryUpdate()
  → IUiDispatcher.InvokeAsync(...)  // 回到 UI 线程
  → ObservableCollection 更新 → DataGrid 刷新
  → IUiActivityTracker.Record(DataChange, ...)
```

### 2.2 Agent 路径（MCP）

```
用户输入
  → ChatSession.SendAsync(text)
      → BuildSystemPrompt()：系统提示 + UI 活动日志摘要（唤醒时读取，不推送）
      → IChatClient.GetResponseAsync(messages, ChatOptions{ Tools = McpClientTool[] })
      → 响应里出现 FunctionCallContent
      → tool.CallAsync(args) ──(内存管道/MCP 协议)──► MCP Server
            → OrderTools.SearchOrdersAsync(...)   // 手写语义封装：校验/兜底/日志
                → OrderServiceBridge.SearchAsync(...)   // 源生成器产出
                    → IOrderService（OrdersViewModel）.SearchAsync(...)
                        → IOrderRepository.GetAll()
                        → IUiDispatcher.InvokeAsync(...)  // UI 同步
      → FunctionResultContent 回灌给模型 → 循环直到没有工具调用
      → 返回 AiChatReply(Text, ToolCalls)
```

两条路径最终汇聚在 `OrdersViewModel` 的同一段业务逻辑上——这是本架构「UI 与 Agent 平权」的核心。

### 2.3 进程内 MCP 的建立顺序

1. `McpServerBridge` 创建两对内存管道（`System.IO.Pipelines`）；
2. `AddMcpServer().WithInProcessStreamTransport(bridge)` 把服务端接在管道一端（MCP Server 作为 HostedService）；
3. `host.StartAsync()` 启动 MCP Server；
4. `bridge.ConnectAsync()` 创建 `McpClient` 接在另一端，并调用 `ListToolsAsync()` 拉取工具清单；
5. `ChatSession` 从这里取得 `McpClientTool`（继承自 `AIFunction`）交给 `IChatClient`。

> 顺序不能反：先启动 Server 再建 Client，否则 initialize 握手会一直等待。

---

## 3. 源生成器规则

输入：所有「引用了 `AI.Extension.Abstractions`」的程序集里，标注了 `[AiContract]` 的接口。
实现：`CompilationProvider` → 扫描当前编译 + 引用程序集的元数据（`ForAttributeWithMetadataName` 只能看到当前编译的语法树，看不到 `Demo.Contract` 这种引用程序集）。

### 3.1 标注

| 特性 | 位置 | 作用 |
| --- | --- | --- |
| `[AiContract(Name, Lifetime, GenerateRegistration)]` | interface | 标记契约；`Lifetime` 决定桥接的 DI 生命周期（默认 Transient） |
| `[AiTool(Description, Name)]` | method | 给 AI 的语义描述 + 建议工具名（进入契约清单，最终工具名由封装的 `[McpServerTool(Name)]` 决定） |
| `[AiIgnore]` | method / parameter | 不桥接该方法；或从生成签名中省略该可选参数 |

### 3.2 产物

每个契约生成：

1. **桥接类** `{ContractName}Bridge`（命名空间 `AI.Extension.Generated.{契约命名空间}`）：
   - 强类型构造函数注入契约实现；
   - 实现 `IAiContractBridge`（暴露 `ContractType` / `ContractName` / `Methods` 元数据）；
   - 为每个可桥接方法生成一等转发方法（保留返回类型、可空注解、默认参数）。
2. **`AddAiContractBridges()`**（`AI.Extension.Generated` 命名空间）：
   - 按 `Lifetime` 注册所有桥接（`TryAdd`，幂等）；
   - 注册 `AiContractCatalog` 单例——UI 诊断面板与测试可直接枚举「当前暴露了哪些 AI 契约」。
3. **`Describe()`**：不依赖 DI 的契约清单，供测试/文档生成使用。

### 3.3 诊断

| ID | 级别 | 场景 |
| --- | --- | --- |
| AIEX001 | Warning | 方法含泛型参数或 `ref/out/in` 参数，已跳过（AI 工具无法表达） |
| AIEX002 | Warning | 契约接口对生成程序集不可见 |
| AIEX003 | Warning | `[AiContract]` 标在了非 interface 上 |
| AIEX004 | Info | 契约没有可桥接的方法 |
| AIEX005 | Info | 契约成员不是方法，已忽略 |

---

## 4. DI 生命周期与注册顺序

| 服务 | 生命周期 | 说明 |
| --- | --- | --- |
| `IOrderRepository` | Singleton | 内存仓储（真实项目替换为数据层） |
| `OrdersViewModel` | Singleton | 同时作为 UI 数据源与 `IOrderService` 实现 |
| `IOrderService` | Singleton → 指向同一个 ViewModel | 保证 UI 与 AI 看到同一份状态 |
| `OrderServiceBridge` | Transient（契约上可配） | 无状态转发器，避免捕获依赖问题 |
| `AI.Extension.Mcp.McpServerBridge` | Singleton | 进程内 MCP 会话 |
| `IChatClient` | Singleton | 真实模型或离线客户端 |
| `ChatSession` | Singleton | 串行化对话（内部 `SemaphoreSlim`） |
| `IUiDispatcher` | Singleton | WPF：`WpfUiDispatcher`；无界面：`ImmediateUiDispatcher` |
| `IUiActivityTracker` | Singleton | 环形缓冲，容量 200 |

> 注册顺序注意：`IUiDispatcher` 是 `TryAdd` 的「默认实现」，WPF 侧必须**先注册** `WpfUiDispatcher`（`AddDemoCoreForWpf()` 已封装这一点），否则会被 `ImmediateUiDispatcher` 抢占。

---

## 5. 线程模型

- `ChatSession` 的工具循环运行在 MCP 管道的后台线程；
- 工具 → 桥接 → ViewModel 全部可能落在后台线程；
- 任何修改 `ObservableCollection` / 绑定属性的地方都必须经过 `IUiDispatcher`；
- WPF 实现：`Dispatcher.InvokeAsync(...).Task.Unwrap()`（非阻塞，避免 `.Wait()` 死锁）；
- Blazor 实现：默认 `ImmediateUiDispatcher` + 组件层 `InvokeAsync(StateHasChanged)`（骨架阶段的简化，见 `docs/BLAZOR.md`）。

---

## 6. UI 上下文日志（反向流）

- **不做**：把 UI 事件实时推给模型；
- **做**：`IUiActivityTracker.Record(category, description, detail)` 写入固定容量环形缓冲（加锁，O(1)，不阻塞 UI 线程）；
- 记录的是**语义**（「用户选中订单 SO-1002」），不是原始事件（`IsMouseOver=true`）；
- 每次 `ChatSession.SendAsync` 构造系统提示时，读取最近 N 条渲染成文本注入（N 由 `AiChatOptions.UiContextEntryCount` 控制）。

---

## 7. 与最初设计对话的对应关系

| 设计对话中的点 | 本骨架的落点 |
| --- | --- |
| 契约 → 源生成器 → MCP 基础设施 | `Demo.Contract` → `AI.Extension.Abstractions.Generator` → `Demo.MCP` 里的 `OrderServiceBridge` |
| 类型映射/JSON Schema 交给微软 MCP 框架 | 全部由 `[McpServerTool]`/`[Description]` + SDK 生成，生成器不碰 Schema |
| 用户手写轻量封装（语义与 ViewModel 不同） | `Demo.MCP/OrderTools.cs`：改名、扁平入参、校验、异常兜底、写日志 |
| Demo.Core 是 WPF 的 DI 启动项（Swagger 式） | `DemoBootstrap.AddDemoCore()/AddDemoCoreForWpf()`，宿主只写一行 |
| Demo.MCP 只引 Abstractions 即可生成 | 生成器扫描引用程序集元数据；源码树里需额外把 Generator 作为 Analyzer 引用（打包后由 NuGet analyzers 目录提供） |
| UI 反向流不推送、记录日志唤醒时读取 | `UiActivityTracker` + `ChatSession.BuildSystemPrompt()` |
| 线程亲和性不能硬编码 Dispatcher | `IUiDispatcher` 抽象，WPF/Blazor 各自实现 |
| 会话/流式并发安全 | `ChatSession` 用 `SemaphoreSlim` 串行化；MCP 会话单客户端 |

### 已知偏离

1. **`Demo.MCP` 引用了 `Demo.Contract`**（`PrivateAssets=all`，仅编译期）：强类型桥接必须能看见契约类型。若不希望 MCP 依赖契约程序集，可让生成器把桥接产出到独立的 `Demo.Contract.Generated` 程序集。
2. **没有独立的 `Demo.WpfStartup`**：按最初设计的命名保留 `Demo.Core` 作为组合根，WPF 宿主入口是 `Demo.App`。
3. **`AI.Extension.Blazor` 与 `Demo.BlazorHost`** 属于并行的 Blazor 分支，说明见 `docs/BLAZOR.md`。

---

## 8. 测试策略

| 测试 | 覆盖 |
| --- | --- |
| `AiContractGeneratorTests` | 用 `CSharpGeneratorDriver` 直接驱动生成器：产物、`[AiIgnore]`、泛型跳过诊断、生成代码可编译 |
| `ContractGenerationTests` | `Describe()` 契约清单；桥接转发到契约实现 |
| `McpRoundTripTests` | 真实 Host + 真实 MCP 协议：工具枚举、参数校验错误、取消订单写回仓储、对话驱动工具、UI 上下文注入 |
| `OrdersViewModelTests` | 契约实现的业务行为（过滤、取消规则、统计）与 UI 日志记录 |
| `UiActivityTrackerTests` | 环形缓冲容量/顺序/截断、上下文摘要格式 |

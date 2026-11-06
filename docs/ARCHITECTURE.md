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
    │           │              └──► Demo.MCP ──► Demo.Contract        │
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
- **依赖方向是 MCP 层 ──► 契约层**：`Demo.MCP` 主动引用 `Demo.Contract` 与 `AI.Extension.Abstractions`；`Demo.Contract` 反过来不引用任何 AI/MCP 包，也没有任何 AI 特性。
- **`Demo.MCP` 不引用 `Demo.ViewModel`**：通过 DI 拿到的 `IOrderService` 实现（实际是 ViewModel），实现类对 MCP 层不可见。
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

**依赖方向**：MCP 层用 `[AiContractBinding(typeof(业务契约接口))]` 主动声明「要桥接哪个契约」；
契约层不引用 `AI.Extension.Abstractions`、不打任何 AI 特性——AI 能力对业务是纯外部附加。

实现：`context.SyntaxProvider.ForAttributeWithMetadataName(...)` 只扫描 MCP 程序集里的声明类（语法级增量），
再从 `typeof(...)` 解析出的接口符号读取方法签名；因此不需要扫描引用程序集，也不需要 `CompilationProvider` 全量遍历。

### 3.1 标注（全部位于 MCP 层）

| 特性 | 位置 | 作用 |
| --- | --- | --- |
| `[AiContractBinding(typeof(IOrderService), Name, Lifetime, GenerateRegistration)]` | class（MCP 层声明类） | 声明要桥接的契约；`Lifetime` 决定桥接的 DI 生命周期（默认 Transient） |
| `[AiMethod(nameof(IOrderService.SearchAsync), ToolName = "order_search", Description = "...")]` | 同一 class，可多个 | 登记「契约方法 → 工具名/描述」的追踪元数据，进入契约清单；**不直接影响模型**（工具语义仍由 `[McpServerTool]` 决定），但可以让测试断言「契约映射的工具名」与「MCP 实际暴露的工具名」没有漂移 |

契约层（`Demo.Contract`）：普通 C# 接口，零 AI 依赖、零 AI 特性。

### 3.2 产物

每个绑定声明生成：

1. **桥接类** `{ContractName}Bridge`（命名空间 `AI.Extension.Generated.{契约命名空间}`）：
   - 强类型构造函数注入契约实现；
   - 实现 `IAiContractBridge`（暴露 `ContractType` / `ContractName` / `Methods` 元数据）；
   - 为该契约的每个可桥接方法生成一等转发方法（保留返回类型、可空注解、默认参数）。
2. **`AddAiContractBridges()`**（`AI.Extension.Generated` 命名空间）：
   - 按 `Lifetime` 注册所有桥接（`TryAdd`，幂等）；
   - 注册 `AiContractCatalog` 单例——UI 诊断面板与测试可直接枚举「当前暴露了哪些 AI 契约」。
3. **`Describe()`**：不依赖 DI 的契约清单，供测试/文档生成使用。

### 3.3 诊断

| ID | 级别 | 场景 |
| --- | --- | --- |
| AIEX001 | Warning | 契约方法含泛型参数、`ref/out/in` 参数，或为静态成员，已跳过（AI 工具无法表达） |
| AIEX002 | Warning | `[AiContractBinding]` 的 `typeof(...)` 不是可解析的接口 |
| AIEX003 | Info | 契约没有可桥接的方法 |
| AIEX004 | Info | 契约成员不是方法，已忽略 |
| AIEX005 | Warning | `[AiMethod]` 指向了契约中不存在的方法 |
| AIEX006 | Warning | 重复的桥接声明（同一契约或同名桥接只生成一次） |

---

## 4. DI 生命周期与注册顺序

| 服务 | 生命周期 | 说明 |
| --- | --- | --- |
| `IOrderRepository` | Singleton | 内存仓储（真实项目替换为数据层） |
| `OrdersViewModel` | Singleton | 同时作为 UI 数据源与 `IOrderService` 实现 |
| `IOrderService` | Singleton → 指向同一个 ViewModel | 保证 UI 与 AI 看到同一份状态 |
| `OrderServiceBridge` | Transient（`[AiContractBinding]` 上可配） | 无状态转发器，避免捕获依赖问题 |
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
| 契约 → 源生成器 → MCP 基础设施 | MCP 层 `[AiContractBinding(typeof(IOrderService))]` → `AI.Extension.Abstractions.Generator` → `Demo.MCP` 里的 `OrderServiceBridge` |
| 类型映射/JSON Schema 交给微软 MCP 框架 | 全部由 `[McpServerTool]`/`[Description]` + SDK 生成，生成器不碰 Schema |
| 用户手写轻量封装（语义与 ViewModel 不同） | `Demo.MCP/OrderTools.cs`：改名、扁平入参、校验、异常兜底、写日志 |
| Demo.Core 是 WPF 的 DI 启动项（Swagger 式） | `DemoBootstrap.AddDemoCore()/AddDemoCoreForWpf()`，宿主只写一行 |
| 契约层不能被 AI 污染（依赖方向是 MCP → 契约） | `Demo.Contract` 不引用 `AI.Extension.Abstractions`，零 AI 特性；绑定声明在 `Demo.MCP/Ai/OrderServiceBinding.cs` |
| UI 反向流不推送、记录日志唤醒时读取 | `UiActivityTracker` + `ChatSession.BuildSystemPrompt()` |
| 线程亲和性不能硬编码 Dispatcher | `IUiDispatcher` 抽象，WPF/Blazor 各自实现 |
| 会话/流式并发安全 | `ChatSession` 用 `SemaphoreSlim` 串行化；MCP 会话单客户端 |

### 已知偏离

1. **绑定声明与契约分属两个程序集**：这是本骨架对原设计的一处修正——原设计把 `[AiContract]` 打在契约接口上，会让契约层反向依赖 AI 抽象。现在契约层零依赖，MCP 层用 `[AiContractBinding]` 主动绑定。
2. **没有独立的 `Demo.WpfStartup`**：按最初设计的命名保留 `Demo.Core` 作为组合根，WPF 宿主入口是 `Demo.App`。
3. **`AI.Extension.Blazor` 与 `Demo.BlazorHost`** 属于并行的 Blazor 分支，说明见 `docs/BLAZOR.md`。

---

## 8. 测试策略

| 测试 | 覆盖 |
| --- | --- |
| `AiContractGeneratorTests` | 用 `CSharpGeneratorDriver` 直接驱动生成器：MCP 层绑定声明 → 产物、契约层零 AI 特性、泛型跳过诊断、非接口目标诊断、生成代码可编译 |
| `ContractGenerationTests` | `Describe()` 契约清单；桥接转发到契约实现 |
| `McpRoundTripTests` | 真实 Host + 真实 MCP 协议：工具枚举、参数校验错误、取消订单写回仓储、对话驱动工具、UI 上下文注入 |
| `OrdersViewModelTests` | 契约实现的业务行为（过滤、取消规则、统计）与 UI 日志记录 |
| `UiActivityTrackerTests` | 环形缓冲容量/顺序/截断、上下文摘要格式 |


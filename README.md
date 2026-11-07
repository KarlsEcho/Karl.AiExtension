# Karl.AiExtension
<img width="1678" height="515" alt="image" src="https://github.com/user-attachments/assets/447c58fb-dca7-43ba-9609-a4b6e26666d1" />


把「契约驱动 + 源生成器 + 微软 MCP C# SDK + 轻量语义封装」的桌面端 AI 扩展架构，落成一个**可以跑起来**的 .NET 10 解决方案骨架。

> 目标不是做一个玩具 Demo，而是把架构里的每一条边界都变成真实代码：
> UI 线程亲和性、MCP 进程内宿主、源生成器产物、契约/工具对齐、UI 上下文日志、工具调用轨迹。

---

## 1. 这套架构在解决什么

传统桌面应用（WPF/WinForms/WinUI）接入 AI 的常见做法是：在 ViewModel 里拼 Prompt、写死 Function Calling JSON、或者用反射强扫 ViewModel 暴露工具。结果是 UI 语义被 AI 绑架、协议细节泄漏到业务层、存量代码难以 AI 化。

本方案把职责切成四条互不污染的通路：

```
人类操作：   界面点击 ──► ViewModel / Command ──┐
                                              ├──► 业务逻辑（同一套）
AI 操作：    自然语言 ──► 大模型 ──► MCP 工具 ──┘

契约：       Demo.Contract（纯业务接口，零实现、零 AI 依赖）
绑定声明：   Demo.MCP 的 [AiContractBinding]（MCP 层主动声明「桥接哪个契约、映射成哪些工具」）
基础设施：   AI.Extension.Abstractions.Generator（按声明在 MCP 层产出强类型桥接 + DI 注册）
协议：       微软 MCP C# SDK（[McpServerTool] / [Description] / JSON Schema）
语义隔离：   Demo.MCP（开发者手写的轻量封装：改名、扁平入参、校验、异常兜底、权限拦截）
上下文：     UI 活动环形日志，AI 被唤醒时读取（不主动推送）
供应商：     AI.Extension.Core.Providers（多供应商/模型列表/DPAPI 加密持久化/运行时切换 IChatClient）
```

关键设计取舍：

| 决策 | 原因 |
| --- | --- |
| 源生成器只生成「桥接基础设施」，不生成 `[McpServerTool]` | 工具语义必须由人写：ViewModel 的语义（操作）≠ 给 AI 的语义（意图），中间必须有一层薄封装 |
| JSON Schema / 工具注册交给微软 MCP C# SDK | 协议、参数反序列化、Schema 生成是 SDK 的强项，自己写只会引入 bug |
| MCP Server 跑在应用进程内（内存管道） | 工具要直接访问应用单例（ViewModel/仓储），又要保留 MCP 协议边界；不需要起子进程 |
| UI 线程调度抽象 `IUiDispatcher` | 工具调用在后台线程，ViewModel 绑定在 UI 线程；抽象后同一套 ViewModel 可被 WPF / Blazor 复用 |
| UI 历史用环形日志 + 唤醒时读取 | 主动推送会 Token 爆炸、噪音淹没有效信息，还会拖垮 UI 线程 |
| 供应商/模型与「对话行为」分离 | 供应商配置要能持久化、加密、运行时切换；系统提示等对话行为与它无关，混在一起会导致每次换模型都要重配提示词 |

---

## 2. 项目结构

```
src/                                    ← 框架本体
  AI.Extension.Abstractions/            绑定声明特性（[AiContractBinding] / [AiMethod]）、桥接接口、UI 抽象
  AI.Extension.Abstractions.Generator/  Roslyn 增量源生成器（按 MCP 层的绑定声明产出桥接）
  AI.Extension.Core/                    会话核心：工具循环、进程内 MCP 宿主、UI 日志、供应商/模型管理、MVVM 基类；<br>一键接入门面 `AddAiExtension(options => ...)`
  AI.Extension.WPF/                     WPF 外壳：聊天面板、模型与供应商面板、工具/契约诊断面板、UI 可替换视图工厂、WpfUiDispatcher
  AI.Extension.Blazor/                  Blazor 外壳（Razor 组件，与 WPF 共享 ChatSession）
demo/                                   ← 演示/参考实现（不参与框架包）
  Demo.Contract/                        纯业务契约（IOrderService），零实现、零 AI 依赖
  Demo.Model/                           领域模型 + 内存仓储
  Demo.ViewModel/                       OrdersViewModel：实现契约，UI 与 AI 共用的业务入口
  Demo.MCP/                             手写语义封装 OrderTools（[McpServerToolType]）+ 生成器产出的桥接
  Demo.Core/                            组合根：AddDemoCore() / AddDemoCoreForWpf()（Swagger 式注册）
  Demo.View/                            WPF 视图（MainWindow）
  Demo.App/                             WPF 宿主入口（最薄的一层）
  Demo.Host/                            控制台宿主：闭环验证 + `--stdio` 标准 MCP Server
  Demo.BlazorHost/                      Blazor Server 演示宿主（订单表格 + 聊天面板，端口 5180）
tests/
  AI.Extension.Abstractions.Generator.Tests/  生成器单测（CSharpGeneratorDriver 驱动）
  Demo.Tests/                                  生成链路、MCP 闭环、ViewModel、UI 日志测试
docs/
  ARCHITECTURE.md                        依赖图、时序、生成规则、扩展指引
  BLAZOR.md                              Blazor 分支说明（由 Blazor 分支实现者维护）
```

> 框架代码只在 `src/`；`demo/` 是可直接运行/参考的样例，复制框架给业务项目时**不要带 demo**。


依赖方向（箭头表示「引用」；关键约束：**契约层不引用任何 AI/MCP 包**）：

```
Demo.App ──► Demo.Core ──► AI.Extension.WPF ──► AI.Extension.Core ──► AI.Extension.Abstractions
   │             │                                     ▲
   │             ├──► Demo.MCP ──► Demo.Contract ──────┼── (MCP 主动依赖契约，反向不存在)
   │             ├──► Demo.ViewModel ──► Demo.Contract │
   │             └──► Demo.Model                       │
   └──► Demo.View ──► Demo.ViewModel / AI.Extension.WPF

Demo.MCP 引用 AI.Extension.Abstractions（编写绑定声明）与 AI.Extension.Abstractions.Generator（Analyzer）
```

---

## 3. 快速开始

前置：.NET 10 SDK（本仓库用 10.0.402 验证）。

```powershell
dotnet build                                  # 构建全部项目
dotnet test                                   # 生成器 + 业务/闭环测试
```

### 3.1 看闭环：控制台宿主（推荐先跑这个）

```powershell
dotnet run --project demo/Demo.Host
```

它会：建容器 → 启动进程内 MCP Server → 连接进程内 MCP Client → 列出 8 个工具 → 直接调用工具 → 走一轮完整对话（统计 / 查详情 / 取消订单），并打印工具调用轨迹。

诊断命令（不启动 MCP）：

```powershell
dotnet run --project demo/Demo.Host -- --check-providers   # 列出供应商并对已启用者测试连通性/拉取模型
dotnet run --project demo/Demo.Host -- --chat "帮我新增一个客户叫测试客户，金额 100"   # 用真实模型跑一轮工具调用
```

### 3.2 看界面：WPF 宿主

```powershell
dotnet run --project demo/Demo.App
```

左侧是传统订单表格（人类路径），右侧是 AI 面板（Agent 路径）。

- 表格下方有「新增 / 编辑订单」区：填写客户、金额、备注后点「新增订单」；选中行会自动带入内容，点「保存到选中」「删除选中」即可改/删数据（AI 与 UI 走同一段业务逻辑）；
- 右栏可以直接说：「帮我统计一下现在的订单情况」「看看 SO-1002」「取消 SO-1001，原因是客户重复下单」「把 SO-1002 标记为已支付」「新增一个客户叫云栖科技金额 128000」「删除 SO-1006」；
- 每次 AI 调用 MCP 工具，工具调用轨迹会实时显示，**左侧表格同步变化**；
- 「工具与契约」页签展示源生成器产出的契约清单、MCP 实际暴露的 8 个工具、以及 UI 上下文环形日志。

无界面自检（CI 可用，会验证一次真实的「创建 → 删除」写链路）：

```powershell
demo\Demo.App\bin\Debug\net10.0-windows\Demo.App.exe --smoke
# 结果写入同目录 smoke.log
```

### 3.3 把工具暴露给外部 MCP 宿主（stdio / HTTP）

**WPF 应用内直接开**：右侧「工具与契约」页签 →「对外暴露 MCP」→ 填端口 →「开启暴露」。
同进程内启动一个只监听 `127.0.0.1` 的 Streamable HTTP 端点（默认 `http://127.0.0.1:5199`），
默认关闭、可随时关闭/重开，**不需要再单独启动 Demo.Host**，harness 里能直接看到应用当前的数据。

无界面场景仍可用 `Demo.Host`：

```powershell
# 本地 harness（stdio）：Claude Desktop / Claude Code / CodeBuddy / WorkBuddy / Cursor / VS Code Copilot…
dotnet publish demo/Demo.Host -c Release -o publish
publish\Demo.Host.exe --stdio          # stdout 是协议通道，别用 dotnet run 直接接

# 无界面 HTTP（不开 WPF 应用时）
dotnet run --project demo/Demo.Host -- --http   # http://127.0.0.1:5199
```

配置模板、各 harness 的配置位置、WorkBuddy/CodeBuddy 的接入方式与验证步骤见
[`docs/MCP-HOSTS.md`](docs/MCP-HOSTS.md)。

> 同一套 `[McpServerTool]` 工具既能被应用内 AI 调用，也能被任意 MCP harness 调用，不需要改 MCP 层代码。

### 3.4 接入真实大模型（供应商 / 模型管理）

不写配置文件也能用：WPF 右侧「**模型与供应商**」页签内置了 DeepSeek / OpenAI / 阿里云百炼 / 本地 Ollama 预设。

1. 选中供应商 → 填 API Key（落盘时用 Windows DPAPI 加密）→ 勾选「启用」→「保存」；
2. 需要的话点「测试连接」「拉取模型」自动补全模型列表；
3. 点「设为当前模型」立即生效（无需重启）；「清除当前模型」回到离线兜底。

配置保存在 `%APPDATA%\Karl.AiExtension\providers.json`（可用 `KARL_AI_DATA_DIR` 改目录），WPF / Blazor / 控制台共享同一份。
内置预设带版本号：升级后启动时会自动把旧安装里的预设迁移到最新端点/模型，**保留你填的 API Key 与启用状态**。
也支持免配置文件的环境变量启动（首次运行会导入成一个名为「环境变量导入」的供应商）：

```powershell
$env:KARL_AI_ENDPOINT = "https://api.deepseek.com"
$env:KARL_AI_API_KEY  = "sk-..."
$env:KARL_AI_MODEL    = "deepseek-flash"
```

> DeepSeek 参数以官方 2026 文档为准：OpenAI 格式 base_url = `https://api.deepseek.com`（不带 `/v1`），
> 当前模型为 `deepseek-flash` / `deepseek-v4-pro`（旧名 `deepseek-chat` / `deepseek-reasoner` 已下线）。
> 可用 `dotnet run --project demo/Demo.Host -- --check-providers` 直接验证端点与模型。

未配置任何可用供应商时，`IChatClientAccessor` 会退回兜底客户端（`Demo.Core` 注入离线规则客户端），保证骨架永远可运行。

### 3.5 会话管理（多标签 + 持久化）

右侧「AI 对话」支持**多标签会话**：每个标签是独立上下文，可以同时和多个会话聊天；
子智能体也能通过 `IChatSessionFactory` 各自创建会话（`CreateNew()` / `OpenAsync(id)`），与界面共用同一套存储。

- **默认存储**：`{程序目录}/chat/history/`，**一个会话一个 JSON 文件**：
  - `using/`：未归档会话（启动时恢复为标签页，可筛选/单独查看）；
  - `stored/`：已归档会话（「归档当前」把文件移到这里，不再出现在标签里）。
  - 例如：`chat/history/using/自定义标题`_`{conversationId}.json`、`chat/history/stored/...`；
    文件名格式为 **`{会话名称}_{Id}.json`**（标题里非法字符替换为下划线；改名时文件跟随重命名）；
  - 旧版汇总文件（`using.json` / `store.json`）首次启动会自动拆分迁移，原文件保留为 `*.legacy` 备份。
- **流式输出**：AI 回复逐字显示；生成中可点「停止」保留已生成内容；
  供应商/客户端不支持流式时自动回退为一次性返回（Blazor 面板暂未接流式）；
- **每轮对话自动保存**；**首轮结束后由 AI 总结一个会话标题**（失败时回退为首条消息截断）；
  双击标签标题、或点「重命名」按钮可修改标题并持久化；
- 关闭标签**不会丢记录**：可在顶部「打开会话」下拉里重新打开（**下拉只列未归档会话**；归档会话只在 `stored/` 文件夹里）；
- 「删除当前」永久删除会话记录（弹窗确认）；
- **存储目录可通过 DI 配置**：

```csharp
services.AddAiExtensionWpf(options =>
{
    options.UseMcpToolsFrom(typeof(OrderTools).Assembly);

    // 自定义会话存储目录（默认 {AppContext.BaseDirectory}/chat/history）
    options.ConversationStorePath = @"D:\OpenWASO\chat\history";

    // 或者完全自定义存储实现（数据库/远端）：
    // options.ConversationStore = new MyConversationStore();

    // 标题生成（默认开启，20 字以内；关闭后回退为首条消息截断）
    // options.ChatOptions.AutoTitleFromFirstRound = false;
    // options.ChatOptions.AutoTitleMaxLength = 20;
});
```

> 程序目录不可写（如装在 Program Files）时请显式配置 `ConversationStorePath` 到可写目录。

### 3.6 Blazor 宿主

```powershell
dotnet run --project demo/Demo.BlazorHost   # http://localhost:5180
```

订单表格 + AI 聊天面板 + 工具清单 +（供应商/模型切换），复用同一套 `ChatSession` / `Demo.Core` / 进程内 MCP 与同一份供应商配置。
与 WPF 的差异（线程调度、`SynchronizationContext`、数据绑定、预渲染）与已知限制见 [`docs/BLAZOR.md`](docs/BLAZOR.md)。
---

## 4. 新增一个 AI 能力（5 步）

以「客户管理」为例：

1. **写契约（纯业务，零 AI 依赖）**：在 `Demo.Contract` 加接口，只写业务方法，**不打任何 AI 特性**；
2. **实现契约**：在 `Demo.ViewModel` 里实现接口（这就是 UI 与 AI 共用的业务入口）；
3. **注册实现**：在 `Demo.Core` 的 `AddDemoDomain()` 里把接口映射到实现；
4. **MCP 层声明绑定**：在 `Demo.MCP` 加一个声明类，`[AiContractBinding(typeof(ICustomerService))]`（可选 `[AiMethod(nameof(...), ToolName = ...)]` 登记工具对齐信息）；
5. **生成桥接**：编译即可——生成器在 `Demo.MCP` 里产出 `CustomerServiceBridge` 并加进 `AddAiContractBridges()`；
6. **写语义封装**：在 `Demo.MCP` 加 `[McpServerToolType]` 类，方法上标 `[McpServerTool(Name = "customer_search")]` + `[Description]`，方法体内调用 `CustomerServiceBridge`。

依赖方向始终是 **MCP 层 ──► 契约层**：契约层不知道 AI 的存在，也不需要为了 AI 改一行代码。

不需要手写任何 MCP 协议代码，也不需要手写 JSON Schema。

---

## 5. 当前边界（骨架阶段的诚实说明）

- **流式输出**：会话目前是「一次完整回复」，`ChatSession` 预留了工具轨迹上报，但未做逐 Token 推送；`IChatClient` 支持流式，接入即可。
- **供应商管理**：内置 4 个 OpenAI 兼容预设，支持运行时增删改、测试连接、拉取模型、切换当前模型；API Key 用 Windows DPAPI 加密后落盘（非 Windows 平台降级为 Base64 混淆，需换成平台密钥链）。内置预设带版本号，升级会自动迁移旧配置（保留 Key 与启用状态）。
- **人工确认（Human-in-the-loop）**：敏感工具目前只做参数校验与日志，未做「AI 请求 → 原生弹窗确认」的拦截；`ChatSession` 的工具循环是显式写的，加这一步只需在调用前插入一次用户确认。
- **权限模型**：没有 RBAC，只有工具级校验。
- **数据操作**：订单示例支持增删改查——查询（`order_search` / `order_get` / `order_summary`）、改（`order_update` / `order_change_status` / `order_cancel`）、增（`order_create`）、删（`order_delete`，必须显式 `confirm=true`）；WPF 表格下方也有对应的新增/编辑/删除表单。
- **契约层零 AI 依赖**：`Demo.Contract` 不引用 `AI.Extension.Abstractions`（见 csproj），「暴露成哪些工具、工具名、描述」全部在 `Demo.MCP` 声明与实现。
- **生成器增量性能**：绑定声明与 MCP 在同一程序集，已用语法级 `ForAttributeWithMetadataName` 增量 Provider；契约方法签名从元数据读取，契约规模很大时可再加一层按接口缓存的包装。
- **Blazor**：已完成 Interactive Server 宿主（`Demo.BlazorHost`）；WASM / 多 circuit 多会话隔离等限制见 `docs/BLAZOR.md`。
- **`Demo.MCP` 对 `Demo.Contract` 的引用是设计意图**：依赖方向 `MCP 层 ──► 契约层`，MCP 主动依赖契约；契约层不需要为 AI 改一行代码。

---

## 6. 设计文档

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)：依赖图、调用时序、源生成器规则、DI 生命周期、线程模型、扩展指引
- [`docs/ADOPTION.md`](docs/ADOPTION.md)：**把框架接入存量 WPF 项目的 6 步迁移指南**（含分阶段推进、常见坑、参考实现）
- [`docs/MCP-HOSTS.md`](docs/MCP-HOSTS.md)：**把 MCP 层交给 Claude Code / CodeBuddy / WorkBuddy / Cursor 等 harness 使用**（stdio / HTTP 两种传输的配置与边界）
- [`docs/BLAZOR.md`](docs/BLAZOR.md)：Blazor 分支如何复用同一套核心


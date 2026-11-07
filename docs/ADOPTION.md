# 把一个存量 WPF 项目接入这套框架

> 目标：**不动存量业务代码**，只给希望在 AI 时代被「点名操作」的能力加一层外部适配。建议按「6 步最小可用 → 3 个阶段扩大」推进，第一个可演示闭环通常 0.5~1 天。

## 0. 先判断能不能直接接

| 现状 | 结论 |
| --- | --- |
| .NET 8 / 9 / 10 的 WPF 项目 | ✅ 直接接（本框架目标 `net10.0` / `net10.0-windows`） |
| .NET Framework 4.8 / .NET Core 3.x | ⚠️ 需先升级项目到 .NET 8+（框架库无法被旧 TFM 引用）。若短期不能升级，只能把 MCP 层做成独立进程（stdio），但**AI 改数据无法实时回写现有 UI**，体验会打折 |
| 没有 DI | ✅ 可接：引入 `Microsoft.Extensions.Hosting` 作为组合根（本仓库 `Demo.App` 就只有 40 行启动代码） |
| 已有 Prism / CommunityToolkit.Mvvm / 自研 MVVM | ✅ 可接：契约实现就是普通方法，命令体系不用换 |

需要准备的东西只有三样：**一个能跑的 DI 容器**、**你的业务服务/ViewModel**、**一个窗口放 AI 面板的位置**。

---

## 第 1 步：引入框架（15 分钟）

两种方式，二选一：

**A. 源码树直接引用（本仓库的用法）**

```xml
<ItemGroup>
  <ProjectReference Include="..\framework\AI.Extension.Abstractions\AI.Extension.Abstractions.csproj" />
  <ProjectReference Include="..\framework\AI.Extension.Core\AI.Extension.Core.csproj" />
  <ProjectReference Include="..\framework\AI.Extension.WPF\AI.Extension.WPF.csproj" />
</ItemGroup>
```

**B. 打包成 NuGet 后引用**

```xml
<PackageReference Include="AI.Extension.Abstractions" Version="0.1.0" />
<PackageReference Include="AI.Extension.Core" Version="0.1.0" />
<PackageReference Include="AI.Extension.WPF" Version="0.1.0" />
```

> ⚠️ **源码树方式下生成器不会自动传递**：MCP 项目里必须显式把它当 Analyzer 引用，否则契约桥接不会生成：
>
> ```xml
> <ProjectReference Include="..\framework\AI.Extension.Abstractions.Generator\AI.Extension.Abstractions.Generator.csproj"
>                   OutputItemType="Analyzer"
>                   ReferenceOutputAssembly="false" />
> ```

---

## 第 2 步：抽「契约」——只抽 2~3 个高频场景（30 分钟）

契约是**普通业务接口**：不引 AI 包、不打任何 AI 特性、参数扁平、返回可序列化 DTO。

```csharp
// 你的项目里的任意位置（建议单独一个 Xxx.Contract 项目）
public interface ICustomerService
{
    Task<IReadOnlyList<Customer>> SearchAsync(string? keyword, CancellationToken cancellationToken = default);
    Task<OperationResult> CreateAsync(string name, string phone, CancellationToken cancellationToken = default);
}
```

选择标准（很重要）：

- ✅ 高频、语义清晰、参数少（≤ 5 个，尽量扁平）；
- ✅ 幂等/可回滚的查询与常规修改；
- ❌ 先不要碰：删除、支付、审批、批量导出等破坏性或长耗时操作（留到阶段三加确认/权限）。

---

## 第 3 步：让现有 ViewModel / Service 实现契约（30~60 分钟）

```csharp
public sealed class CustomerViewModel : ObservableObject, ICustomerService
{
    // 现有代码完全保留
    public async Task<IReadOnlyList<Customer>> SearchAsync(string? keyword, CancellationToken ct = default)
    {
        Customer[] rows = await _repository.QueryAsync(keyword, ct);

        // ⚠️ 工具调用来自后台线程：任何集合/属性变更都要回 UI 线程
        await _uiDispatcher.InvokeAsync(() =>
        {
            Customers.Clear();
            foreach (Customer row in rows) Customers.Add(row);
            return Task.CompletedTask;
        });

        return rows;
    }

    public async Task<OperationResult> CreateAsync(string name, string phone, CancellationToken ct = default)
    {
        // 复用现有业务逻辑，不要为 AI 另写一套
        Customer created = await _repository.CreateAsync(name, phone, ct);
        await _uiDispatcher.InvokeAsync(() => { Customers.Add(created); return Task.CompletedTask; });
        return new OperationResult(true, $"已创建客户 {created.Name}", created);
    }
}
```

DI 注册（把接口指向同一个实例，保证 UI 与 AI 看到同一份状态）：

```csharp
services.AddSingleton<CustomerViewModel>();
services.AddSingleton<ICustomerService>(sp => sp.GetRequiredService<CustomerViewModel>());
```

---

## 第 4 步：MCP 层「声明 + 生成 + 语义封装」（每个契约约 30 分钟）

新建一个 `Xxx.Mcp` 项目（或放在现有项目里的一个文件夹），依赖：

```xml
<ProjectReference Include="..\Your.Contract\Your.Contract.csproj" />
<ProjectReference Include="..\AI.Extension.Abstractions\AI.Extension.Abstractions.csproj" />
<ProjectReference Include="..\AI.Extension.Abstractions.Generator\...csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<PackageReference Include="ModelContextProtocol" Version="2.3.1" />
```

**4.1 声明绑定**（MCP 层主动依赖契约；契约层对 AI 无感知）：

```csharp
[AiContractBinding(typeof(ICustomerService))]
[AiMethod(nameof(ICustomerService.SearchAsync), ToolName = "customer_search",
    Description = "按关键词搜索客户，返回客户列表。")]
[AiMethod(nameof(ICustomerService.CreateAsync), ToolName = "customer_create",
    Description = "新增客户：姓名与手机号。")]
internal sealed class CustomerServiceBinding;
```

编译后生成器会自动产出 `CustomerServiceBridge`（强类型转发）与 `AddAiContractBridges()`。

**4.2 手写语义封装**（改名/扁平入参/校验/异常兜底/写日志都在这一层）：

```csharp
[McpServerToolType]
public sealed class CustomerTools(CustomerServiceBridge bridge, IUiActivityTracker tracker)
{
    [McpServerTool(Name = "customer_search")]
    [Description("按关键词搜索客户，返回姓名、手机号、等级。")]
    public async Task<object> SearchAsync(
        [Description("关键词，可留空")] string? keyword = null,
        CancellationToken ct = default)
    {
        tracker.Record(UiActivityCategory.AgentAction, $"AI 调用 customer_search（keyword={keyword}）");
        try
        {
            IReadOnlyList<Customer> customers = await bridge.SearchAsync(keyword, ct);
            return customers.Select(c => new { c.Name, c.Phone, c.Level }).ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return new { success = false, error = ex.Message }; } // 让模型能读懂并自我纠正
    }
}
```

---

## 第 5 步：宿主接线（10 分钟，一行配置）

### 5.1 一行接入

```csharp
// 现有业务注册保持不动
services.AddSingleton<CustomerViewModel>();
services.AddSingleton<ICustomerService>(sp => sp.GetRequiredService<CustomerViewModel>());

// 框架：一行接入（WPF 外壳 + 供应商 + 会话 + 契约桥接 + 进程内 MCP + 自动启动）
services.AddAiExtensionWpf(options =>
{
    options.UseMcpToolsFrom(typeof(CustomerTools).Assembly);   // ← 只需要告诉它工具在哪个程序集
    options.ChatOptions.SystemPrompt = "你是客户管理助手……";      // 可选
    options.FallbackChatClient = new OfflineDemoChatClient();   // 可选：无 Key 时的兜底
});
```

这一行内部自动完成：

| 原本要手写的 | 门面自动处理 |
| --- | --- |
| `AddAiProviderManagement()` | ✅ 供应商持久化 + 密钥保护 |
| `AddAiChatSession()` | ✅ 会话 + 当前客户端访问器 |
| `AddAiContractBridges()` | ✅ 通过工具程序集上的生成器标记自动登记 |
| 创建 `McpServerBridge` + `AddMcpServer().WithInProcessStreamTransport()` | ✅ 自动完成 |
| `WithToolsFromAssembly(...)` | ✅ 用 `UseMcpToolsFrom` 的程序集 |
| `InitializeAsync` / `ConnectAsync` 的调用时序 | ✅ 由框架 HostedService 在 `StartAsync` 内完成 |

### 5.2 启动

```csharp
var host = builder.Build();
await host.StartAsync();          // 框架自动：加载供应商配置 → 启动 MCP Server → 连接 MCP Client
new MainWindow(...).Show();
```

**不需要**再手写 `InitializeAsync()` / `ConnectAsync()`。⚠️ 前提是使用 Generic Host（`Host.CreateApplicationBuilder`）；
纯 `Application` 启动、不建 Host 的存量项目，需先引入 Host 作为组合根（见第 0 步判断表）。

### 5.3 把面板塞进现有窗口

```xml
<TabControl>
    <TabItem Header="AI 对话"><wpf:AiChatPanel x:Name="ChatSurface" /></TabItem>
    <TabItem Header="模型与供应商"><wpf:AiProviderManagerPanel x:Name="ProvidersSurface" /></TabItem>
    <TabItem Header="工具与契约"><wpf:AiDiagnosticsPanel x:Name="DiagnosticsSurface" /></TabItem>
</TabControl>
```

```csharp
ChatSurface.DataContext = host.Services.GetRequiredService<ChatPanelViewModel>();
ProvidersSurface.DataContext = host.Services.GetRequiredService<ProviderManagerViewModel>();
DiagnosticsSurface.DataContext = host.Services.GetRequiredService<AiDiagnosticsViewModel>();
```

### 5.4 需要自己控制装配？细粒度 API 仍然可用

门面只是把下面这组调用打包了，高级场景（不用 Generic Host、多个 MCP Server、自定义启动顺序）可以手动拼装：

```csharp
services.AddAiExtensionWpf();                    // 只装外壳
services.AddAiProviderManagement(store);         // 供应商
services.AddAiChatSession(chatOptions, fallback); // 会话
services.AddAiContractBridges();                 // 生成的桥接（手动调用）
services.AddMcpServer().WithInProcessStreamTransport(bridge).WithToolsFromAssembly(asm);

await providerManager.InitializeAsync();
await host.StartAsync();
await bridge.ConnectAsync();
```

---

## 第 6 步：配模型 + 冒烟验证（30 分钟）

1. 打开「模型与供应商」：选预设（DeepSeek / OpenAI / 通义 / Ollama）→ 填 API Key → 启用 → 保存 → 设为当前模型；
   或直接设环境变量 `KARL_AI_ENDPOINT / KARL_AI_API_KEY / KARL_AI_MODEL`。
2. 验证清单：
   - [ ] 「工具与契约」页能看到 N 个工具（与 `[McpServerTool]` 数量一致）；
   - [ ] 对话「查一下…」→ 工具轨迹出现、表格同步；
   - [ ] 对话「新增/修改…」→ 数据落库、UI 刷新；
   - [ ] 故意传错参数 → 返回可读错误，模型能自我纠正；
   - [ ] 无 Key 时回退兜底客户端，应用不崩。
3. 回归测试建议：
   - 契约程序集**不引用** `AI.Extension.Abstractions`（架构约束测试）；
   - `AddAiContractBridges()` 能解析出桥接；
   - 每个工具名与 `[AiMethod(ToolName = ...)]` 一致（防契约/工具漂移）。

---

## 分阶段推进（不要一次全量）

| 阶段 | 范围 | 产出 | 预算 |
| --- | --- | --- | --- |
| 阶段一 · 试点 | 1 个服务 + 2~3 个查询工具 | 可演示的「AI 查数据并同步界面」闭环 | 0.5~1 天 |
| 阶段二 · 扩大 | 增加写操作（新增/修改）；接真实模型；加诊断面板 | 日常高频操作可由 AI 完成 | 1~2 天 |
| 阶段三 · 生产化 | 敏感操作确认（HITL）、权限/审计、流式输出、数据持久化、多会话隔离 | 可交付给最终用户 | 按需 |

**新增一个能力的固定动作**：写接口方法 → 现有 VM/Service 实现 → 加 `[AiMethod]` 映射 → 写 `[McpServerTool]` 封装。不需要改生成器、不需要写 MCP 协议代码。

---

## 常见坑（存量项目最容易踩）

1. **UI 线程**：MCP 工具在后台线程执行，`ObservableCollection`/绑定属性必须在 `IUiDispatcher.InvokeAsync` 里改。WPF 下务必先 `AddAiExtensionWpf()` 再注册业务（`TryAdd` 顺序）。
2. **不要直接暴露 ViewModel 全部方法**：ViewModel 的语义是「当前 UI 状态下的操作」，AI 需要的是「带上下文参数的意图」——语义封装层不能省。
3. **DI 生命周期**：ViewModel/服务用 Singleton，桥接用 Transient（默认），`IAiChatClientAccessor`/`ChatSession` Singleton。
4. **源生成器不传递**：源码树里 MCP 项目必须显式 Analyzer 引用；打包后由 Abstractions 包的 `analyzers/dotnet/cs` 自动提供。
5. **危险操作**：删除/支付等先不要给 AI，或至少加 `confirm=true` 参数并在封装层拦截；阶段三再加原生确认弹窗。
6. **契约别膨胀**：一个契约对应一个业务域（Customer/Order/Report），工具数量控制在单个域 5~8 个以内，模型选择工具更稳。
7. **数据结构**：工具入参用扁平值类型/字符串，避免嵌套对象与 `Stream`/`IntPtr` 等无法表达的签名。

---

## 参考实现

本仓库就是「按上面步骤做出来」的完整样例：

- 契约：[`Demo.Contract/IOrderService.cs`](../demo/Demo.Contract/IOrderService.cs)
- 实现（UI + AI 共用）：[`Demo.ViewModel/OrdersViewModel.cs`](../demo/Demo.ViewModel/OrdersViewModel.cs)
- MCP 绑定与语义封装：[`Demo.MCP/Ai/OrderServiceBinding.cs`](../demo/Demo.MCP/Ai/OrderServiceBinding.cs)、[`Demo.MCP/OrderTools.cs`](../demo/Demo.MCP/OrderTools.cs)
- 组合根：[`Demo.Core/DemoBootstrap.cs`](../demo/Demo.Core/DemoBootstrap.cs)
- WPF 宿主与面板：[`Demo.App/App.xaml.cs`](../demo/Demo.App/App.xaml.cs)、[`Demo.View/MainWindow.xaml`](../demo/Demo.View/MainWindow.xaml)


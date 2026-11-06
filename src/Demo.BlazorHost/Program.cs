using AI.Extension.Blazor;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Demo.BlazorHost.Components;
using Demo.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

var builder = WebApplication.CreateBuilder(args);

// 固定端口：文档、脚本与人工验证都以 http://localhost:5180 为准。
builder.WebHost.UseUrls("http://localhost:5180");

// ---------------------------------------------------------------------------
// 组合根：业务 + AI 会话 + 进程内 MCP Server（与 Demo.Host / WPF 完全相同的一行注册）
// ---------------------------------------------------------------------------
builder.Services.AddDemoCore();

// Blazor 外壳自己的 DI 入口：UI 上下文追踪 + Immediate 调度（TryAdd，不会覆盖自定义实现）。
// ChatSession 仍由 AddDemoCore 注册，保证 Blazor 与 WPF/控制台共用同一个会话核心。
builder.Services.AddAiExtensionBlazor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ---------------------------------------------------------------------------
// 关键启动顺序（与 Demo.Host 的 StartDemoHostAsync 同构）：
// 0) 先加载供应商配置：页面渲染/AiModelSelector 读取 Providers 都依赖它（首次运行会写内置预设）；
// 1) 启动宿主：进程内 MCP Server 作为 IHostedService 随宿主一起启动；
// 2) 再连接进程内 MCP Client 并拉取工具清单（ChatSession 依赖 bridge.Tools）；
// 3) 最后进入运行循环，开始接受 HTTP 请求。
//
// 注意：StartAsync 之后不能再调用 app.Run()，因为 Run() 内部会再次调用 StartAsync，
// 而 Kestrel 会抛 InvalidOperationException「Server has already started.」。
// 所以这里用 WaitForShutdownAsync 阻塞到进程退出，语义等价于「StartAsync 之后的 Run」。
// ---------------------------------------------------------------------------
await app.Services.GetRequiredService<IAiProviderManager>().InitializeAsync();

await app.StartAsync();

McpServerBridge bridge = app.Services.GetRequiredService<McpServerBridge>();
IReadOnlyList<McpClientTool> tools = await bridge.ConnectAsync(app.Services.GetService<ILoggerFactory>());
app.Logger.LogInformation(
    "进程内 MCP 已连接（会话 {SessionId}），发现 {Count} 个工具：{ToolNames}",
    bridge.Client.SessionId,
    tools.Count,
    string.Join(", ", tools.Select(static tool => tool.Name)));

// 宿主已经在监听，这里只需等待 Ctrl+C / SIGTERM。
await app.WaitForShutdownAsync();

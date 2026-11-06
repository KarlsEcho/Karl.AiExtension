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
// 启动顺序说明：供应商配置加载、进程内 MCP Server 启动、MCP Client 连接，
// 全部由框架注册的 HostedService 在 StartAsync 内自动完成（见 AddAiExtension 门面）。
// 这里只需要 StartAsync + 保持运行。
//
// 注意：StartAsync 之后不能再调用 app.Run()，因为 Run() 内部会再次调用 StartAsync，
// 而 Kestrel 会抛 InvalidOperationException「Server has already started.」。
// 所以这里用 WaitForShutdownAsync 阻塞到进程退出，语义等价于「StartAsync 之后的 Run」。
// ---------------------------------------------------------------------------
await app.StartAsync();

McpServerBridge bridge = app.Services.GetRequiredService<McpServerBridge>();
app.Logger.LogInformation(
    "进程内 MCP 已连接（会话 {SessionId}），发现 {Count} 个工具：{ToolNames}",
    bridge.Client.SessionId,
    bridge.Tools.Count,
    string.Join(", ", bridge.Tools.Select(static tool => tool.Name)));

// 宿主已经在监听，这里只需等待 Ctrl+C / SIGTERM。
await app.WaitForShutdownAsync();

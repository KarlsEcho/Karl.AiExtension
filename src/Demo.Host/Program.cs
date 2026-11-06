using System.Text;
using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Demo.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// 模式 0：供应商诊断（不启动 MCP）
//   dotnet run --project src/Demo.Host -- --check-providers
//   读取 %APPDATA%\Karl.AiExtension\providers.json，列出供应商并对已启用者测试连通性。
// ---------------------------------------------------------------------------
if (args.Contains("--check-providers", StringComparer.OrdinalIgnoreCase))
{
    var services = new ServiceCollection();
    services.AddLogging(builder => builder.AddConsole());
    services.AddAiProviderManagement();

    await using ServiceProvider provider = services.BuildServiceProvider();
    IAiProviderManager manager = provider.GetRequiredService<IAiProviderManager>();
    await manager.InitializeAsync();

    Console.WriteLine($"配置文件：{JsonAiProviderStore.GetDefaultPath()}");
    Console.WriteLine($"供应商：{manager.Providers.Count} 个，当前模型：{manager.ActiveModelLabel}\n");

    foreach (AiProviderDefinition definition in manager.Providers)
    {
        string keyState = string.IsNullOrWhiteSpace(definition.ApiKey) ? "未填写" : "已填写";
        Console.WriteLine($"[{definition.DisplayName}] id={definition.Id} enabled={definition.Enabled} endpoint={definition.Endpoint} key={keyState}");
        Console.WriteLine($"    models: {string.Join(", ", definition.Models.Select(static model => model.Id))}");

        if (!definition.Enabled)
        {
            Console.WriteLine("    （未启用，跳过连通性测试）");
            continue;
        }

        bool reachable = await manager.TestConnectionAsync(definition.Id);
        Console.WriteLine($"    测试连接: {(reachable ? "成功" : "失败")}");

        if (reachable)
        {
            IReadOnlyList<string> discovered = await manager.DiscoverModelsAsync(definition.Id);
            Console.WriteLine($"    端点返回模型: {string.Join(", ", discovered)}");
        }
    }

    return;
}

// ---------------------------------------------------------------------------
// 模式 1：标准 MCP Server（stdio）
//   dotnet run --project src/Demo.Host -- --stdio
//   可被 Claude Desktop / Claude Code / VS Code Copilot / CodeBuddy / WorkBuddy 等
//   支持 stdio 的 MCP 宿主接入。
// ---------------------------------------------------------------------------
if (args.Contains("--stdio", StringComparer.OrdinalIgnoreCase))
{
    var mcpBuilder = Host.CreateApplicationBuilder(args);
    mcpBuilder.Logging.ClearProviders();
    mcpBuilder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    mcpBuilder.Services.AddDemoStdioServer();

    await mcpBuilder.Build().RunAsync();
    return;
}

// ---------------------------------------------------------------------------
// 模式 1.2：Streamable HTTP MCP Server
//   dotnet run --project src/Demo.Host -- --http
//   端点：http://127.0.0.1:5199  （供只支持 URL/HTTP 的 harness 或远端客户端使用）
// ---------------------------------------------------------------------------
if (args.Contains("--http", StringComparer.OrdinalIgnoreCase))
{
    var httpBuilder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
    httpBuilder.WebHost.UseUrls("http://127.0.0.1:5199");
    httpBuilder.Logging.ClearProviders();
    httpBuilder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Warning);

    httpBuilder.Services.AddDemoMcpServer().WithHttpTransport(options =>
    {
        // 无状态模式：不需要服务端主动 sampling/elicitation，部署最简
        options.Stateless = true;
    });

    var app = httpBuilder.Build();
    app.MapMcp();

    Console.WriteLine("MCP Streamable HTTP Server: http://127.0.0.1:5199");
    await app.RunAsync();
    return;
}

// ---------------------------------------------------------------------------
// 模式 1.5：真实模型单轮对话（使用当前配置的供应商；会调用 MCP 工具）
//   dotnet run --project src/Demo.Host -- --chat "帮我新增一个客户叫测试客户，金额 100"
// ---------------------------------------------------------------------------
int chatArgumentIndex = Array.FindIndex(args, argument => string.Equals(argument, "--chat", StringComparison.OrdinalIgnoreCase));
if (chatArgumentIndex >= 0)
{
    string prompt = chatArgumentIndex + 1 < args.Length
        ? string.Join(' ', args.Skip(chatArgumentIndex + 1))
        : "帮我统计一下现在的订单情况";

    using IHost chatHost = await DemoBootstrap.StartDemoHostAsync();
    IAiProviderManager chatProviderManager = chatHost.Services.GetRequiredService<IAiProviderManager>();
    Console.WriteLine($"当前模型：{chatProviderManager.ActiveModelLabel}");

    if (!chatProviderManager.HasActiveModel)
    {
        Console.WriteLine("（未配置可用供应商，本次使用离线规则客户端）");
    }

    var chatSession = chatHost.Services.GetRequiredService<ChatSession>();
    Console.WriteLine($"用户：{prompt}");
    AiChatReply chatReply = await chatSession.SendAsync(prompt);

    foreach (AiToolCallTrace trace in chatReply.ToolCalls)
    {
        Console.WriteLine($"  ↳ 工具 {trace.ToolName}（{(trace.Success ? "成功" : "失败")}，{trace.Duration.TotalMilliseconds:F0} ms）");
    }

    Console.WriteLine($"AI：{chatReply.Text}");
    return;
}

// ---------------------------------------------------------------------------
// 模式 2：进程内闭环演示（默认）
//   建容器 → 启动进程内 MCP Server → 连接进程内 MCP Client → 列工具 / 调工具 / 走一轮对话
// ---------------------------------------------------------------------------
Console.WriteLine("=== Karl.AiExtension · 进程内 MCP 闭环验证 ===\n");

using IHost host = await DemoBootstrap.StartDemoHostAsync();

IAiProviderManager providerManager = host.Services.GetRequiredService<IAiProviderManager>();
Console.WriteLine($"AI 供应商：{providerManager.Providers.Count} 个配置，当前模型：{providerManager.ActiveModelLabel}");

McpServerBridge bridge = host.Services.GetRequiredService<McpServerBridge>();
IReadOnlyList<McpClientTool> tools = bridge.Tools;

Console.WriteLine($"已连接进程内 MCP Server，会话 ID：{bridge.Client.SessionId}");
Console.WriteLine($"发现工具 {tools.Count} 个：\n");
foreach (McpClientTool tool in tools)
{
    Console.WriteLine($"  • {tool.Name}  —  {tool.Description}");
}

Console.WriteLine();
Console.WriteLine("--- 直接调用 order_summary ---");
CallToolResult summary = await bridge.Client.CallToolAsync("order_summary", new Dictionary<string, object?>());
Console.WriteLine(FormatToolResult(summary));

Console.WriteLine();
Console.WriteLine("--- 直接调用 order_search（status=Paid）---");
CallToolResult paid = await bridge.Client.CallToolAsync(
    "order_search",
    new Dictionary<string, object?> { ["status"] = "Paid" });
Console.WriteLine(FormatToolResult(paid));

Console.WriteLine();
Console.WriteLine("--- 边界：传入非法状态，封装层应返回可读错误而不是抛异常 ---");
CallToolResult invalid = await bridge.Client.CallToolAsync(
    "order_search",
    new Dictionary<string, object?> { ["status"] = "NotAStatus" });
Console.WriteLine(FormatToolResult(invalid));

Console.WriteLine();
Console.WriteLine("=== 走一轮完整 AI 对话（离线规则模型 → MCP → 语义封装 → ViewModel）===");

ChatSession session = host.Services.GetRequiredService<ChatSession>();
string[] prompts =
[
    "帮我统计一下现在的订单情况",
    "看看 SO-1002 这个订单",
    "取消 SO-1001，原因是客户重复下单",
];

foreach (string prompt in prompts)
{
    Console.WriteLine($"\n用户：{prompt}");
    AiChatReply reply = await session.SendAsync(prompt);
    Console.WriteLine($"AI  ：{reply.Text}");
    foreach (AiToolCallTrace trace in reply.ToolCalls)
    {
        Console.WriteLine(
            $"      ↳ 工具 {trace.ToolName}（{(trace.Success ? "成功" : "失败")}，{trace.Duration.TotalMilliseconds:F0} ms）参数 {trace.ArgumentsJson}");
    }
}

Console.WriteLine();
Console.WriteLine("=== 闭环完成：AI 的工具调用已经把状态写回了 ViewModel / 仓储 ===");

Console.WriteLine("[提示] 配置 KARL_AI_API_KEY / KARL_AI_MODEL 后，同一条链路会改由真实大模型驱动。");
Console.WriteLine("[提示] 运行 `dotnet run --project src/Demo.Host -- --stdio` 可把同一套工具暴露给任意 MCP 宿主。");

static string FormatToolResult(CallToolResult result)
{
    string text = string.Join(
        "\n",
        result.Content.OfType<TextContentBlock>().Select(static block => block.Text));

    return result.IsError is true ? $"[工具返回错误]\n{text}" : text;
}



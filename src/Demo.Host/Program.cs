using System.Text;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Demo.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

Console.OutputEncoding = Encoding.UTF8;

// ---------------------------------------------------------------------------
// 模式 1：标准 MCP Server（stdio）
//   dotnet run --project src/Demo.Host -- --stdio
//   可被 Claude Desktop / VS Code Copilot 等任意 MCP 宿主接入。
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


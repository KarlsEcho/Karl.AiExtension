using AI.Extension.Chat;
using AI.Extension.Mcp;
using Demo.Core;
using Demo.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Demo.Tests;

/// <summary>
/// 进程内 MCP 闭环测试：MCP Client → MCP Server → 手写语义封装 → 生成的桥接 → ViewModel/仓储。
/// </summary>
public sealed class McpRoundTripTests
{
    [Fact]
    public async Task Tools_AreExposedOverMcpProtocol()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        Assert.True(scope.Bridge.IsConnected);
        Assert.Equal(
            new[] { "order_cancel", "order_get", "order_search", "order_summary" },
            scope.Bridge.Tools.Select(static tool => tool.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray());
        Assert.All(scope.Bridge.Tools, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description)));
    }

    [Fact]
    public async Task SummaryTool_ReturnsAggregatedData()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        CallToolResult result = await scope.Bridge.Client.CallToolAsync(
            "order_summary",
            new Dictionary<string, object?>());

        string text = GetText(result);
        Assert.Null(result.IsError);
        Assert.Contains("\"total\":5", text, StringComparison.Ordinal);
        Assert.Contains("229500", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidStatus_IsRejectedBySemanticWrapperWithReadableError()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        CallToolResult result = await scope.Bridge.Client.CallToolAsync(
            "order_search",
            new Dictionary<string, object?> { ["status"] = "NotAStatus" });

        string text = GetText(result);
        Assert.Contains("\"success\":false", text, StringComparison.Ordinal);
        Assert.Contains("NotAStatus", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelTool_MutatesRepositoryThroughContractBridge()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        CallToolResult result = await scope.Bridge.Client.CallToolAsync(
            "order_cancel",
            new Dictionary<string, object?>
            {
                ["orderId"] = "SO-1001",
                ["reason"] = "集成测试取消",
            });

        Assert.Contains("\"success\":true", GetText(result), StringComparison.Ordinal);

        var repository = scope.Host.Services.GetRequiredService<IOrderRepository>();
        Assert.Equal(OrderStatus.Cancelled, repository.Find("SO-1001")!.Status);
    }

    [Fact]
    public async Task ChatSession_DrivesToolsAndReportsTrace()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        var session = scope.Host.Services.GetRequiredService<ChatSession>();
        AiChatReply reply = await session.SendAsync("帮我统计一下现在的订单情况");

        AiToolCallTrace trace = Assert.Single(reply.ToolCalls);
        Assert.Equal("order_summary", trace.ToolName);
        Assert.True(trace.Success);
        Assert.Contains("\"total\":5", trace.ResultPreview, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(reply.Text));
    }

    [Fact]
    public async Task ChatSession_InjectsUiActivityContextIntoPrompt()
    {
        await using DemoHostScope scope = await DemoHostScope.StartAsync();

        var tracker = scope.Host.Services.GetRequiredService<AI.Extension.IUiActivityTracker>();
        tracker.Record(AI.Extension.UiActivityCategory.Selection, "用户选中订单 SO-1002");

        var session = scope.Host.Services.GetRequiredService<ChatSession>();
        await session.SendAsync("看看这个订单");

        string context = tracker.BuildContextSummary();
        Assert.Contains("SO-1002", context, StringComparison.Ordinal);
    }

    private static string GetText(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(static block => block.Text));

    private sealed class DemoHostScope : IAsyncDisposable
    {
        private DemoHostScope(IHost host, McpServerBridge bridge)
        {
            Host = host;
            Bridge = bridge;
        }

        public IHost Host { get; }

        public McpServerBridge Bridge { get; }

        public static async Task<DemoHostScope> StartAsync()
        {
            // 用内存存储隔离本机供应商配置，保证测试始终走离线规则客户端
            IHost host = await DemoBootstrap.StartDemoHostAsync(providerStore: new AI.Extension.Providers.InMemoryAiProviderStore());
            var bridge = host.Services.GetRequiredService<McpServerBridge>();
            return new DemoHostScope(host, bridge);
        }

        public async ValueTask DisposeAsync()
        {
            await Bridge.DisposeAsync();
            await Host.StopAsync();
            Host.Dispose();
        }
    }
}


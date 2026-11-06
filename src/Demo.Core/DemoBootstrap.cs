using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Generated;
using AI.Extension.Mcp;
using Demo.Contract;
using Demo.MCP;
using Demo.Model;
using Demo.ViewModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Demo.Core;

/// <summary>
/// Demo 的组合根（Composition Root）。
/// <para>
/// 这里就是架构图里「Demo.Core 依赖 AI.Extension.WPF，类似 Swagger 的注册方式」的落点：
/// 宿主只写一行 <c>services.AddDemoCore()</c>，底层把
/// 业务层 / 生成器产出的桥接 / AI 会话 / 进程内 MCP Server / 工具封装 全部装好。
/// </para>
/// </summary>
public static class DemoBootstrap
{
    /// <summary>
    /// 注册完整 Demo：业务 + AI 对话 + 进程内 MCP Server（WPF、控制台等进程内宿主使用）。
    /// </summary>
    public static IServiceCollection AddDemoCore(this IServiceCollection services, AiChatOptions? chatOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDemoDomain();

        AiChatOptions options = chatOptions ?? AiChatOptions.FromEnvironment();
        IChatClient chatClient = options.IsRemoteConfigured
            ? AiChatClientFactory.Create(options)
            : new DemoOfflineChatClient();

        services.AddAiChatSession(options, chatClient);

        // 进程内 MCP：同一对内存管道把 Server 与 Client 连起来，工具可直接用容器里的单例
        var bridge = new McpServerBridge();
        services.AddSingleton(bridge);

        IMcpServerBuilder mcp = services.AddMcpServer();
        mcp.WithInProcessStreamTransport(bridge);
        mcp.AddDemoMcpTools();

        return services;
    }

    /// <summary>
    /// 注册完整 Demo（WPF 版）：先用 WPF 的 UI 调度器覆盖核心层默认实现，再注册其余部分。
    /// <para>
    /// 这就是架构图里「Demo.Core 依赖 AI.Extension.WPF、在 DI 里注册」的落点：
    /// UI 框架的选择被收口在组合根，业务与 MCP 层完全不知道宿主是 WPF 还是 Blazor。
    /// </para>
    /// </summary>
    public static IServiceCollection AddDemoCoreForWpf(this IServiceCollection services, AiChatOptions? chatOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionWpf();
        return services.AddDemoCore(chatOptions);
    }

    /// <summary>
    /// 注册「标准 MCP Server（stdio）」形态：不启动 AI 会话，只把工具按 MCP 协议暴露给外部宿主
    /// （Claude Desktop / VS Code Copilot / 任意 MCP 客户端）。
    /// </summary>
    public static IServiceCollection AddDemoStdioServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDemoDomain();
        services.AddAiExtensionCore();

        IMcpServerBuilder mcp = services.AddMcpServer();
        mcp.WithStdioServerTransport();
        mcp.AddDemoMcpTools();

        return services;
    }

    /// <summary>
    /// 快速启动一个进程内 Demo 宿主：建容器 → 启动 MCP Server → 连接进程内 MCP Client。
    /// </summary>
    public static async Task<IHost> StartDemoHostAsync(
        Action<HostApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDemoCore();
        configureBuilder?.Invoke(builder);
        configureServices?.Invoke(builder.Services);

        IHost host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        var bridge = host.Services.GetRequiredService<McpServerBridge>();
        await bridge.ConnectAsync(host.Services.GetService<ILoggerFactory>(), cancellationToken).ConfigureAwait(false);

        return host;
    }

    private static void AddDemoDomain(this IServiceCollection services)
    {
        services.TryAddSingleton<IOrderRepository, InMemoryOrderRepository>();
        services.TryAddSingleton<OrdersViewModel>();
        services.TryAddSingleton<IOrderService>(provider => provider.GetRequiredService<OrdersViewModel>());

        // 源生成器产出的注册入口：AddAiContractBridges()
        services.AddAiContractBridges();
    }
}

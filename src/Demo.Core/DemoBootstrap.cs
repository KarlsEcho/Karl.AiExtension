using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Generated;
using AI.Extension.Mcp;
using AI.Extension.Providers;
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
/// 业务层 / 生成器产出的桥接 / AI 会话 / 供应商模型管理 / 进程内 MCP Server / 工具封装 全部装好。
/// </para>
/// </summary>
public static class DemoBootstrap
{
    /// <summary>
    /// 注册完整 Demo：业务 + 供应商模型管理 + AI 对话 + 进程内 MCP Server（WPF、控制台等进程内宿主使用）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="chatOptions">对话行为配置；为空时使用默认值。</param>
    /// <param name="providerStore">
    /// 供应商配置存储；为空时使用 <c>%APPDATA%\Karl.AiExtension\providers.json</c>。
    /// 测试可传入 <see cref="InMemoryAiProviderStore"/> 以隔离本机配置。
    /// </param>
    public static IServiceCollection AddDemoCore(
        this IServiceCollection services,
        AiChatOptions? chatOptions = null,
        IAiProviderStore? providerStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDemoDomain();
        services.AddAiProviderManagement(providerStore);

        // 未配置任何供应商时使用离线规则客户端；配置后在「模型与供应商」页切换即可自动生效
        services.AddAiChatSession(chatOptions ?? new AiChatOptions(), new DemoOfflineChatClient());

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
    public static IServiceCollection AddDemoCoreForWpf(
        this IServiceCollection services,
        AiChatOptions? chatOptions = null,
        IAiProviderStore? providerStore = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionWpf();
        return services.AddDemoCore(chatOptions, providerStore);
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
    /// 快速启动一个进程内 Demo 宿主：建容器 → 初始化供应商配置 → 启动 MCP Server → 连接进程内 MCP Client。
    /// </summary>
    public static async Task<IHost> StartDemoHostAsync(
        Action<HostApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null,
        IAiProviderStore? providerStore = null,
        CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddDemoCore(providerStore: providerStore);
        configureBuilder?.Invoke(builder);
        configureServices?.Invoke(builder.Services);

        IHost host = builder.Build();

        // 供应商配置先于宿主启动加载：首次运行会写入内置预设（不依赖 MCP Server）
        await host.Services
            .GetRequiredService<IAiProviderManager>()
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

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

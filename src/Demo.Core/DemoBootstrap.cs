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

        // 一键接入：供应商管理 + 会话 + 自动登记契约桥接 + 进程内 MCP Server + 自动启动
        // （等价于手写 AddAiProviderManagement / AddAiChatSession / AddAiContractBridges / AddMcpServer 四步）
        services.AddAiExtension(options =>
        {
            options.UseMcpToolsFrom(DemoMcpExtensions.Assembly);
            options.ChatOptions = chatOptions ?? new AiChatOptions();
            // 未配置任何供应商时使用离线规则客户端；配置后在「模型与供应商」页切换即可自动生效
            options.FallbackChatClient = new DemoOfflineChatClient();
            options.ProviderStore = providerStore;
        });

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

        services.AddDemoDomain();

        // 一行接入：WPF 外壳 + 供应商/会话/桥接/进程内 MCP 全部自动装配
        services.AddAiExtensionWpf(options =>
        {
            options.UseMcpToolsFrom(DemoMcpExtensions.Assembly);
            options.ChatOptions = chatOptions ?? new AiChatOptions();
            options.FallbackChatClient = new DemoOfflineChatClient();
            options.ProviderStore = providerStore;
        });

        return services;
    }

    /// <summary>
    /// 只注册「MCP Server 工具侧」（业务 + AI 扩展核心 + 全部工具），由宿主决定传输层。
    /// 适合 stdio / HTTP / 自定义传输；进程内宿主的完整装配请用 <see cref="AddDemoCore"/>。
    /// </summary>
    public static IMcpServerBuilder AddDemoMcpServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDemoDomain();
        services.AddAiExtensionCore();
        // 只做 MCP Server 的宿主（stdio / HTTP）不经过一键门面，这里显式登记生成的契约桥接
        services.AddAiExtensionContracts(DemoMcpExtensions.Assembly);

        IMcpServerBuilder mcp = services.AddMcpServer();
        mcp.AddDemoMcpTools();
        return mcp;
    }

    /// <summary>
    /// 注册「标准 MCP Server（stdio）」形态：不启动 AI 会话，只把工具按 MCP 协议暴露给外部宿主
    /// （Claude Desktop / Claude Code / VS Code Copilot / CodeBuddy / WorkBuddy 等本地 harness）。
    /// </summary>
    public static IServiceCollection AddDemoStdioServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDemoMcpServer().WithStdioServerTransport();
        return services;
    }

    /// <summary>
    /// 快速启动一个进程内 Demo 宿主：建容器 → 启动宿主（框架自动初始化供应商配置并连接 MCP Client）。
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

        // 供应商初始化 + 进程内 MCP Client 连接由框架注册的 HostedService 在 StartAsync 内完成
        await host.StartAsync(cancellationToken).ConfigureAwait(false);
        return host;
    }

    private static void AddDemoDomain(this IServiceCollection services)
    {
        services.TryAddSingleton<IOrderRepository, InMemoryOrderRepository>();
        services.TryAddSingleton<OrdersViewModel>();
        services.TryAddSingleton<IOrderService>(provider => provider.GetRequiredService<OrdersViewModel>());
    }
}

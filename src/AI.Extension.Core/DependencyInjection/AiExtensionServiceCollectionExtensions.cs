using System.Reflection;
using System.Text.Json;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace AI.Extension;

/// <summary>
/// 「一键接入」配置。
/// <para>
/// 典型用法（宿主只需一行 + 一次 <c>host.StartAsync()</c>）：
/// <code>
/// builder.Services.AddAiExtensionWpf(options =>
/// {
///     options.UseMcpToolsFrom(typeof(OrderTools).Assembly);   // 自动注册契约桥接 + 进程内 MCP Server + 工具
///     options.ChatOptions.SystemPrompt = "……";
/// });
/// </code>
/// </para>
/// </summary>
public sealed class AiExtensionOptions
{
    /// <summary>对话行为配置（系统提示、UI 上下文条数、工具轮次上限）。</summary>
    public AiChatOptions ChatOptions { get; set; } = new();

    /// <summary>未配置供应商时使用的兜底客户端（例如离线演示客户端）。为空时使用内置提示客户端。</summary>
    public IChatClient? FallbackChatClient { get; set; }

    /// <summary>供应商配置存储（测试/绿色版可传内存实现）；为空时用默认 JSON 存储。</summary>
    public IAiProviderStore? ProviderStore { get; set; }

    /// <summary>是否启用 MCP 工具；设置后框架会自动登记契约桥接、创建进程内 MCP Server 并注册工具。</summary>
    public Assembly? McpToolsAssembly { get; private set; }

    /// <summary>工具参数/返回值的 JSON 配置；为空时使用框架默认（camelCase、中文不转义、枚举转字符串）。</summary>
    public JsonSerializerOptions? ToolSerializerOptions { get; set; }

    /// <summary>
    /// 指定包含 MCP 工具（<c>[McpServerToolType]</c>）与契约绑定声明（<c>[AiContractBinding]</c>）的程序集。
    /// 框架会据此自动注册生成的桥接与工具。
    /// </summary>
    public void UseMcpToolsFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        McpToolsAssembly = assembly;
    }
}

/// <summary>
/// 框架的一键接入门面：供应商管理 + 对话会话 + 契约桥接 + 进程内 MCP + 自动启动。
/// <para>
/// 宿主不再需要手动创建 <see cref="McpServerBridge"/>、调用 <c>AddMcpServer()</c>、也不用手工控制
/// 「初始化供应商 → 启动宿主 → 连接 Client」的顺序：这些都由框架注册的
/// <c>IHostedService</c> 在 <c>host.StartAsync()</c> 时自动完成。
/// </para>
/// </summary>
public static class AiExtensionServiceCollectionExtensions
{
    /// <summary>一键接入（无工具，仅供应商管理 + 对话）。</summary>
    public static IServiceCollection AddAiExtension(this IServiceCollection services)
        => services.AddAiExtension(configure: null);

    /// <summary>一键接入。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">配置回调。</param>
    public static IServiceCollection AddAiExtension(
        this IServiceCollection services,
        Action<AiExtensionOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new AiExtensionOptions();
        configure?.Invoke(options);

        // 供应商/模型管理 + 对话会话（底层仍是细粒度 API，保持可单独使用）
        services.AddAiProviderManagement(options.ProviderStore);
        services.AddAiChatSession(options.ChatOptions, options.FallbackChatClient);

        // 工具程序集：自动登记生成的契约桥接，并启动进程内 MCP Server
        if (options.McpToolsAssembly is { } toolsAssembly)
        {
            RegisterGeneratedBridges(services, toolsAssembly);

            var bridge = new McpServerBridge();
            services.AddSingleton(bridge);

            IMcpServerBuilder mcp = services.AddMcpServer();
            mcp.WithInProcessStreamTransport(bridge);
            mcp.WithToolsFromAssembly(
                toolsAssembly,
                options.ToolSerializerOptions ?? AiExtensionJson.CreateToolSerializerOptions());
        }

        // 自动启动：host.StartAsync() 时初始化供应商配置并连接进程内 MCP Client
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, AiExtensionIntegrationHostedService>());

        return services;
    }

    /// <summary>
    /// 从工具程序集上生成的 <see cref="AiExtensionContractAssemblyAttribute"/> 找到桥接注册入口并调用，
    /// 省去宿主手写 <c>services.AddAiContractBridges()</c>。
    /// </summary>
    private static void RegisterGeneratedBridges(IServiceCollection services, Assembly toolsAssembly)
    {
        var attribute = toolsAssembly.GetCustomAttribute<AiExtensionContractAssemblyAttribute>();
        Type? registrationType = attribute?.BridgeRegistrationType;
        if (registrationType is null)
        {
            return;
        }

        MethodInfo? method = registrationType.GetMethod("AddAiContractBridges", [typeof(IServiceCollection)]);
        method?.Invoke(null, [services]);
    }
}

/// <summary>
/// 框架自动启动服务：先加载供应商配置，再连接进程内 MCP Client。
/// 注册顺序在 MCP Server 的 HostedService 之后，因此 <c>StartAsync</c> 执行到这里时 Server 已就绪。
/// </summary>
internal sealed class AiExtensionIntegrationHostedService(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await services
            .GetRequiredService<IAiProviderManager>()
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

        McpServerBridge? bridge = services.GetService<McpServerBridge>();
        if (bridge is not null)
        {
            await bridge
                .ConnectAsync(services.GetService<ILoggerFactory>(), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

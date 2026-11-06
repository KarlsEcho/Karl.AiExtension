using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AI.Extension;

/// <summary>AI.Extension.Core 的 DI 注册入口。</summary>
public static class AiExtensionCoreServiceCollectionExtensions
{
    /// <summary>注册 UI 上下文追踪器与 UI 线程调度抽象（不含聊天与 MCP）。</summary>
    public static IServiceCollection AddAiExtensionCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUiActivityTracker>(_ => new UiActivityTracker());
        services.TryAddSingleton<IUiDispatcher, ImmediateUiDispatcher>();
        return services;
    }

    /// <summary>
    /// 注册供应商/模型管理：持久化存储 + 密钥保护 + <see cref="IAiProviderManager"/>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="store">可选的存储实现（测试/绿色版）；为 null 时使用 JSON 文件存储。</param>
    public static IServiceCollection AddAiProviderManagement(
        this IServiceCollection services,
        IAiProviderStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAiSecretProtector, DpapiSecretProtector>();

        if (store is not null)
        {
            services.TryAddSingleton(store);
        }
        else
        {
            services.TryAddSingleton<IAiProviderStore>(provider => new JsonAiProviderStore(
                filePath: null,
                protector: provider.GetRequiredService<IAiSecretProtector>()));
        }

        services.TryAddSingleton<IAiProviderManager>(provider => new AiProviderManager(
            provider.GetRequiredService<IAiProviderStore>(),
            provider.GetService<ILogger<AiProviderManager>>()));

        return services;
    }

    /// <summary>注册 AI 对话（供应商管理 + 当前客户端访问器 + ChatSession）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">对话行为配置（系统提示、UI 上下文条数、工具轮次上限）。</param>
    /// <param name="fallbackChatClient">
    /// 未配置任何供应商时使用的兜底客户端（例如演示用的离线规则客户端）。
    /// 为空时使用 <see cref="UnconfiguredChatClient"/>。
    /// </param>
    public static IServiceCollection AddAiChatSession(
        this IServiceCollection services,
        AiChatOptions? options = null,
        IChatClient? fallbackChatClient = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionCore();
        services.AddAiProviderManagement();

        services.TryAddSingleton(options ?? new AiChatOptions());
        services.TryAddSingleton<McpServerBridge>(_ => new McpServerBridge());

        IChatClient fallback = fallbackChatClient ?? new UnconfiguredChatClient();
        services.TryAddSingleton(fallback);

        services.TryAddSingleton<IAiChatClientAccessor>(provider => new AiChatClientAccessor(
            provider.GetRequiredService<IAiProviderManager>(),
            provider.GetRequiredService<IChatClient>()));

        services.TryAddSingleton<ChatSession>(provider => new ChatSession(
            provider.GetRequiredService<IAiChatClientAccessor>(),
            provider.GetRequiredService<McpServerBridge>(),
            provider.GetRequiredService<IUiActivityTracker>(),
            provider.GetRequiredService<AiChatOptions>()));

        return services;
    }
}

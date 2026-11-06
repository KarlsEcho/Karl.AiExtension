using AI.Extension.Chat;
using AI.Extension.Mcp;
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

    /// <summary>注册 AI 对话（IChatClient + ChatSession）。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="options">
    /// 对话配置。为 null 时从环境变量读取；
    /// 若 <see cref="AiChatOptions.IsRemoteConfigured"/> 为 false，将使用离线提示客户端。
    /// </param>
    /// <param name="chatClientOverride">可选的自定义 <see cref="IChatClient"/>（例如离线演示客户端、测试替身）。</param>
    public static IServiceCollection AddAiChatSession(
        this IServiceCollection services,
        AiChatOptions? options = null,
        IChatClient? chatClientOverride = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionCore();

        var effectiveOptions = options ?? AiChatOptions.FromEnvironment();
        services.TryAddSingleton(effectiveOptions);

        services.TryAddSingleton<McpServerBridge>(_ => new McpServerBridge());

        services.TryAddSingleton<IChatClient>(provider => chatClientOverride ?? AiChatClientFactory.Create(
            effectiveOptions,
            provider.GetService<ILoggerFactory>()));

        services.TryAddSingleton<ChatSession>(provider => new ChatSession(
            provider.GetRequiredService<IChatClient>(),
            provider.GetRequiredService<McpServerBridge>(),
            provider.GetRequiredService<IUiActivityTracker>(),
            provider.GetRequiredService<AiChatOptions>()));

        return services;
    }
}

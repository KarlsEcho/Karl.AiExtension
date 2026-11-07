using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Mcp;
using AI.Extension.WPF.Providers;
using AI.Extension.WPF.Threading;
using AI.Extension.WPF.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Extension;

/// <summary>AI.Extension.WPF 的 DI 注册入口。</summary>
public static class WpfServiceCollectionExtensions
{
    /// <summary>
    /// 只注册 WPF 外壳：UI 线程调度实现、聊天/供应商/诊断面板的视图模型、视图工厂。
    /// <para>
    /// 适合需要自己控制供应商/会话/MCP 装配的高级场景；一般直接用
    /// <see cref="AddAiExtensionWpf(IServiceCollection, Action{AiExtensionOptions}, Action{AiExtensionUiOptions})"/> 一行接入即可。
    /// </para>
    /// </summary>
    public static IServiceCollection AddAiExtensionWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionCore();
        services.RemoveAll<IUiDispatcher>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();

        services.TryAddSingleton<ChatPanelViewModel>();
        services.TryAddSingleton<ChatTabsViewModel>();
        services.TryAddSingleton<ProviderManagerViewModel>();
        services.TryAddSingleton<AiDiagnosticsViewModel>();

        // 同进程内「对外暴露 MCP」（Streamable HTTP），由诊断面板的开关控制
        services.TryAddSingleton<IMcpHttpExposureService, AiMcpHttpExposureService>();

        // 可替换 UI：槽位配置 + 视图工厂（宿主用 AiChatHost 等宿主控件即可吃到配置）
        services.TryAddSingleton<AiExtensionUiOptions>();
        services.TryAddSingleton<IAiViewFactory, AiViewFactory>();
        return services;
    }

    /// <summary>
    /// **一键接入 WPF 宿主**：WPF 外壳 + 供应商管理 + 对话会话 + 契约桥接 + 进程内 MCP，
    /// 并在 <c>host.StartAsync()</c> 时自动完成「加载供应商配置 → 连接进程内 MCP Client」。
    /// <para>典型用法：</para>
    /// <code>
    /// builder.Services.AddAiExtensionWpf(options =>
    /// {
    ///     options.UseMcpToolsFrom(typeof(OrderTools).Assembly);
    ///     options.ChatOptions.SystemPrompt = "你是订单助手……";
    ///     options.FallbackChatClient = new OfflineDemoChatClient();
    /// }, ui =>
    /// {
    ///     ui.UseChatShell&lt;MyChatShell&gt;();        // 可选：替换内置 UI
    ///     ui.UseConversation&lt;MyConversation&gt;();
    /// });
    /// var host = builder.Build();
    /// await host.StartAsync();   // 其余交给框架
    /// </code>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">AI 扩展配置回调。</param>
    /// <param name="configureUi">可选：UI 槽位/主题配置回调。</param>
    public static IServiceCollection AddAiExtensionWpf(
        this IServiceCollection services,
        Action<AiExtensionOptions> configure,
        Action<AiExtensionUiOptions>? configureUi = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // 先注册 WPF 外壳（UI 调度器必须在核心层 TryAdd 之前就位）
        services.AddAiExtensionWpf();

        if (configureUi is not null)
        {
            var uiOptions = new AiExtensionUiOptions();
            configureUi(uiOptions);
            services.RemoveAll<AiExtensionUiOptions>();
            services.AddSingleton(uiOptions);
        }

        // 再注册供应商/会话/桥接/进程内 MCP 与自动启动服务
        return services.AddAiExtension(configure);
    }
}

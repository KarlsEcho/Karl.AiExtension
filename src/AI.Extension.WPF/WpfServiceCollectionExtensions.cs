using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;
using AI.Extension.WPF.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AI.Extension;

/// <summary>AI.Extension.WPF 的 DI 注册入口。</summary>
public static class WpfServiceCollectionExtensions
{
    /// <summary>
    /// 注册 WPF 外壳：UI 线程调度实现、聊天面板与诊断面板的视图模型。
    /// <para>
    /// 必须在 <c>AddDemoCore()</c> 之前调用（或使用 <c>AddDemoCoreForWpf()</c>）：
    /// 核心层用 TryAdd 注册 ImmediateUiDispatcher，先注册 WPF 实现即可生效。
    /// 这里额外做一次 RemoveAll + Add，保证无论注册顺序如何都以 WPF 调度器为准。
    /// </para>
    /// </summary>
    public static IServiceCollection AddAiExtensionWpf(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAiExtensionCore();
        services.RemoveAll<IUiDispatcher>();
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();

        services.TryAddSingleton<ChatPanelViewModel>();
        services.TryAddSingleton<ProviderManagerViewModel>();
        services.TryAddSingleton<AiDiagnosticsViewModel>();
        return services;
    }
}


using Microsoft.Extensions.DependencyInjection;

namespace AI.Extension.Blazor;

/// <summary>AI.Extension.Blazor 的 DI 注册入口（与 AddDemoCore 一样遵循 Swagger 式一行注册）。</summary>
public static class AiExtensionBlazorServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Blazor 外壳所需的通用服务。
    /// <para>
    /// 与 WPF 外壳的关键差异：Blazor 没有 STA UI 线程，工具调用回写不需要 Dispatcher，
    /// 继续沿用 Core 默认的 <see cref="ImmediateUiDispatcher"/>；真正的线程封送交给组件，
    /// 由 ChatPanel 在渲染上下文里调用 <c>ComponentBase.InvokeAsync(StateHasChanged)</c> 完成。
    /// </para>
    /// <para>
    /// 注意：本方法只注册 UI 上下文/调度等外壳服务，<b>不</b>注册 ChatSession。
    /// 会话与模型客户端属于组合根职责（<c>AddDemoCore()</c> 或 <c>AddAiChatSession()</c>），
    /// 这样 Blazor 宿主才能像 WPF/控制台宿主一样共用同一份 AI 配置与进程内 MCP Server。
    /// </para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>原服务集合，便于链式调用。</returns>
    public static IServiceCollection AddAiExtensionBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TryAdd 注册：宿主若已提供 WPF 风格或其他自定义实现，不会被覆盖。
        services.AddAiExtensionCore();

        return services;
    }
}

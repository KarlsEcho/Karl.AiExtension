using System.Windows;
using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;

namespace AI.Extension.WPF.Ui;

/// <summary>
/// AI 内置 UI 的可替换配置：每个槽位指定一个视图类型，类型必须同时是
/// <see cref="FrameworkElement"/> 且实现对应的 <see cref="IAiView{TViewModel}"/>（泛型方法在编译期检查）。
/// <para>
/// 默认全部使用内置视图；只配需要的槽位即可，其余保持内置实现。
/// 只替换局部内容时，推荐直接把内置视图的 XAML 复制到宿主工程按需修改（内置视图只依赖 public ViewModel）。
/// </para>
/// </summary>
public sealed class AiExtensionUiOptions
{
    /// <summary>AI 对话外壳视图类型（默认 <see cref="AiChatPanel"/>）。</summary>
    public Type? ChatShellView { get; private set; }

    /// <summary>单个会话视图类型（默认 <see cref="ChatConversationView"/>）。</summary>
    public Type? ConversationView { get; private set; }

    /// <summary>模型与供应商视图类型（默认 <see cref="AiProviderManagerPanel"/>）。</summary>
    public Type? ProviderManagerView { get; private set; }

    /// <summary>工具与契约视图类型（默认 <see cref="AiDiagnosticsPanel"/>）。</summary>
    public Type? DiagnosticsView { get; private set; }

    /// <summary>替换 AI 对话外壳（类型必须实现 <see cref="IAiView{TViewModel}"/> 且是 FrameworkElement）。</summary>
    public void UseChatShell<TView>()
        where TView : FrameworkElement, IAiView<ChatTabsViewModel>
        => ChatShellView = typeof(TView);

    /// <summary>替换单个会话视图。</summary>
    public void UseConversation<TView>()
        where TView : FrameworkElement, IAiView<ChatPanelViewModel>
        => ConversationView = typeof(TView);

    /// <summary>替换模型与供应商视图。</summary>
    public void UseProviderManager<TView>()
        where TView : FrameworkElement, IAiView<ProviderManagerViewModel>
        => ProviderManagerView = typeof(TView);

    /// <summary>替换工具与契约视图。</summary>
    public void UseDiagnostics<TView>()
        where TView : FrameworkElement, IAiView<AiDiagnosticsViewModel>
        => DiagnosticsView = typeof(TView);
}

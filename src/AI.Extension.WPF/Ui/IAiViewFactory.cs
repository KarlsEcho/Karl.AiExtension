using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;

namespace AI.Extension.WPF.Ui;

/// <summary>按槽位创建 AI 视图：默认用内置控件，配置了自定义类型时用自定义视图。</summary>
public interface IAiViewFactory
{
    /// <summary>创建 AI 对话外壳视图（DataContext = <see cref="ChatTabsViewModel"/>）。</summary>
    System.Windows.FrameworkElement CreateChatShell(ChatTabsViewModel viewModel);

    /// <summary>创建单个会话视图（DataContext = <see cref="ChatPanelViewModel"/>）。</summary>
    System.Windows.FrameworkElement CreateConversation(ChatPanelViewModel viewModel);

    /// <summary>创建模型与供应商视图。</summary>
    System.Windows.FrameworkElement CreateProviderManager(ProviderManagerViewModel viewModel);

    /// <summary>创建工具与契约视图。</summary>
    System.Windows.FrameworkElement CreateDiagnostics(AiDiagnosticsViewModel viewModel);
}

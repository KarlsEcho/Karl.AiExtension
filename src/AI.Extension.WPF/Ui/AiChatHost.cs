using System.Windows.Controls;
using AI.Extension.WPF.Chat;

namespace AI.Extension.WPF.Ui;

/// <summary>
/// AI 对话外壳宿主：DataContext 绑定 <see cref="ChatTabsViewModel"/>，
/// 内部按 <see cref="IAiViewFactory"/> 创建配置的外壳视图（默认 <see cref="AiChatPanel"/>）。
/// <para>替换 UI 时宿主 XAML 用本控件代替 <see cref="AiChatPanel"/>。</para>
/// </summary>
public sealed class AiChatHost : ContentControl
{
    /// <summary>构造宿主。</summary>
    public AiChatHost()
    {
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not ChatTabsViewModel viewModel)
        {
            Content = null;
            return;
        }

        Content = viewModel.ViewFactory is { } factory
            ? factory.CreateChatShell(viewModel)
            : new AiChatPanel { DataContext = viewModel };
    }
}

/// <summary>单个会话视图宿主：DataContext 绑定 <see cref="ChatPanelViewModel"/>。</summary>
public sealed class AiConversationHost : ContentControl
{
    /// <summary>构造宿主。</summary>
    public AiConversationHost()
    {
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not ChatPanelViewModel viewModel)
        {
            Content = null;
            return;
        }

        Content = viewModel.ViewFactory is { } factory
            ? factory.CreateConversation(viewModel)
            : new ChatConversationView { DataContext = viewModel };
    }
}

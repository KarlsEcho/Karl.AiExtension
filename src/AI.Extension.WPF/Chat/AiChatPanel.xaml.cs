using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AI.Extension.WPF.Chat;

/// <summary>AI 对话外壳：多会话标签容器（每个标签是一个独立的 <see cref="ChatConversationView"/>）。</summary>
public partial class AiChatPanel : UserControl
{
    /// <summary>构造面板。</summary>
    public AiChatPanel()
    {
        InitializeComponent();
    }

    private void TitleEditBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
        }
    }

    private void TitleEditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatPanelViewModel viewModel })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            viewModel.CommitTitleCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            viewModel.CancelEditTitleCommand.Execute(null);
        }
    }
}

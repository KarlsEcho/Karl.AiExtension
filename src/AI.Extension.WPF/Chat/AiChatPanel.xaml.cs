using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AI.Extension.WPF.Chat;

/// <summary>AI 聊天面板（WPF 外壳），WPF 与 Blazor 共享同一套 ChatSession。</summary>
public partial class AiChatPanel : UserControl
{
    /// <summary>构造面板。</summary>
    public AiChatPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatPanelViewModel oldViewModel)
        {
            oldViewModel.Messages.CollectionChanged -= OnMessagesChanged;
        }

        if (e.NewValue is ChatPanelViewModel newViewModel)
        {
            newViewModel.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 新消息到达后滚动到底部
        MessagesScroll?.ScrollToEnd();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            return;
        }

        if (DataContext is ChatPanelViewModel viewModel && viewModel.SendCommand.CanExecute(null))
        {
            e.Handled = true;
            viewModel.SendCommand.Execute(null);
        }
    }
}

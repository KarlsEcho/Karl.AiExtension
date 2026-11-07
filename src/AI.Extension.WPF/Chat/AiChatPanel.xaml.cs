using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AI.Extension.WPF.Chat;

/// <summary>AI 聊天面板（WPF 外壳），WPF 与 Blazor 共享同一套 ChatSession。</summary>
public partial class AiChatPanel : UserControl
{
    private bool _isImeComposing;

    /// <summary>构造面板。</summary>
    public AiChatPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 中文输入法激活时 Enter 会被标记成 Key.ImeProcessed，且组词中的 Enter 是「上屏」。
        // 这里用「组词开始 / 组词提交」两个事件跟踪组词状态，避免把上屏误当成发送。
        TextCompositionManager.AddPreviewTextInputStartHandler(InputBox, OnCompositionStart);
        TextCompositionManager.AddPreviewTextInputHandler(InputBox, OnCompositionCommitted);
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

    private void OnCompositionStart(object sender, TextCompositionEventArgs e) => _isImeComposing = true;

    private void OnCompositionCommitted(object sender, TextCompositionEventArgs e) => _isImeComposing = false;

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // 取消组词后恢复发送能力
            _isImeComposing = false;
            return;
        }

        if (!ChatInputKeyPolicy.ShouldSendOnEnter(e.Key, e.ImeProcessedKey, Keyboard.Modifiers, _isImeComposing))
        {
            return;
        }

        if (DataContext is not ChatPanelViewModel viewModel || !viewModel.SendCommand.CanExecute(null))
        {
            return;
        }

        // 吞掉 Enter，避免 TextBox 再插入一个换行
        e.Handled = true;
        viewModel.SendCommand.Execute(null);
    }
}

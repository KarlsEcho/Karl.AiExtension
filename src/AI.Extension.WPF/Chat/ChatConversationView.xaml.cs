using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AI.Extension.WPF.Ui;

namespace AI.Extension.WPF.Chat;

/// <summary>单个会话视图（消息列表 + 工具轨迹 + 输入框），由 <see cref="AiChatPanel"/> 的多标签容器承载。</summary>
public partial class ChatConversationView : UserControl, IAiView<ChatPanelViewModel>
{
    private bool _isImeComposing;

    /// <inheritdoc />
    public void Attach(ChatPanelViewModel viewModel) => DataContext = viewModel;

    /// <summary>构造视图。</summary>
    public ChatConversationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // 中文输入法激活时 Enter 会被标记成 Key.ImeProcessed，且组词中的 Enter 是「上屏」。
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

    private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 流式追加内容时保持滚动到底部
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


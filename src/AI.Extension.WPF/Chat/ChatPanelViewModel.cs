using System.Collections.ObjectModel;
using AI.Extension.Chat;
using AI.Extension.Mvvm;

namespace AI.Extension.WPF.Chat;

/// <summary>WPF 聊天面板的视图模型。</summary>
public sealed class ChatPanelViewModel : ObservableObject, IDisposable
{
    private readonly ChatSession _session;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAiChatClientAccessor _chatClientAccessor;
    private bool _disposed;
    private string _inputText = string.Empty;
    private bool _isBusy;
    private string _statusText = "就绪：可以问我「帮我统计一下订单情况」";
    private bool _isEditingTitle;
    private string _editingTitle = string.Empty;
    private CancellationTokenSource? _sendCts;

    /// <summary>构造视图模型。</summary>
    public ChatPanelViewModel(
        ChatSession session,
        IUiDispatcher uiDispatcher,
        IAiChatClientAccessor chatClientAccessor)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _chatClientAccessor = chatClientAccessor ?? throw new ArgumentNullException(nameof(chatClientAccessor));

        _chatClientAccessor.Changed += OnChatClientChanged;

        SendCommand = new AsyncRelayCommand(
            _ => SendAsync(),
            _ => !IsBusy && !string.IsNullOrWhiteSpace(InputText));
        ClearCommand = new RelayCommand(_ => Clear());
        CancelSendCommand = new RelayCommand(_ => _sendCts?.Cancel(), _ => IsBusy);
        BeginEditTitleCommand = new RelayCommand(_ => BeginEditTitle());
        CommitTitleCommand = new AsyncRelayCommand(_ => CommitTitleAsync());
        CancelEditTitleCommand = new RelayCommand(_ => IsEditingTitle = false);

        _session.ToolCallCompleted += OnToolCallCompleted;
        _session.TitleChanged += OnTitleChanged;

        // 恢复已持久化的历史消息（多标签/重启后可见）
        foreach (ConversationMessage message in _session.ConversationMessages)
        {
            if (message.Role is "user" or "assistant")
            {
                Messages.Add(new ChatMessageViewModel(message.Role == "user", message.Text, message.Timestamp));
            }
        }

        if (Messages.Count > 0)
        {
            StatusText = $"已恢复会话（{Messages.Count} 条消息）";
        }
    }

    /// <summary>当前生效的模型（供应商 · 模型）。</summary>
    public string ActiveModelText => _chatClientAccessor.DisplayName;

    /// <summary>当前会话对象（供多标签容器做归档/删除）。</summary>
    public ChatSession Session => _session;

    /// <summary>会话标题（首轮结束后由 AI 生成，可双击标签重命名）。</summary>
    public string Title => _session.ConversationTitle ?? "新对话";

    /// <summary>是否处于标题编辑状态（标签头上显示输入框）。</summary>
    public bool IsEditingTitle
    {
        get => _isEditingTitle;
        private set => SetProperty(ref _isEditingTitle, value);
    }

    /// <summary>编辑中的标题文本。</summary>
    public string EditingTitle
    {
        get => _editingTitle;
        set => SetProperty(ref _editingTitle, value);
    }

    /// <summary>开始编辑标题。</summary>
    public RelayCommand BeginEditTitleCommand { get; }

    /// <summary>提交标题修改。</summary>
    public AsyncRelayCommand CommitTitleCommand { get; }

    /// <summary>取消标题修改。</summary>
    public RelayCommand CancelEditTitleCommand { get; }

    /// <summary>对话消息。</summary>
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    /// <summary>本轮对话的工具调用轨迹。</summary>
    public ObservableCollection<AiToolCallTrace> ToolCalls { get; } = new();

    /// <summary>输入框文本。</summary>
    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>是否正在请求模型。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SendCommand.RaiseCanExecuteChanged();
                CancelSendCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>状态栏文案。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>发送命令。</summary>
    public AsyncRelayCommand SendCommand { get; }

    /// <summary>停止生成命令。</summary>
    public RelayCommand CancelSendCommand { get; }

    /// <summary>新对话命令。</summary>
    public RelayCommand ClearCommand { get; }

    private async Task SendAsync()
    {
        string text = InputText.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        InputText = string.Empty;
        Messages.Add(new ChatMessageViewModel(isUser: true, text));
        var assistantMessage = new ChatMessageViewModel(isUser: false, string.Empty);
        Messages.Add(assistantMessage);
        ToolCalls.Clear();
        IsBusy = true;
        StatusText = "AI 正在生成…";
        _sendCts = new CancellationTokenSource();

        void OnTextDelta(string delta) => _ = AppendDeltaAsync(assistantMessage, delta);

        _session.TextDelta += OnTextDelta;
        try
        {
            AiChatReply reply = await _session.SendStreamingAsync(text, _sendCts.Token).ConfigureAwait(true);

            // 流式累计与最终文本不一致时（例如中途回退到非流式），以最终结果为准
            if (!string.IsNullOrEmpty(reply.Text) && !string.Equals(assistantMessage.Text, reply.Text, StringComparison.Ordinal))
            {
                assistantMessage.SetText(reply.Text);
            }

            StatusText = reply.ToolCalls.Count == 0
                ? "完成（本轮未调用工具）"
                : $"完成（调用 {reply.ToolCalls.Count} 次工具）";
        }
        catch (OperationCanceledException)
        {
            StatusText = "已停止生成";
            if (string.IsNullOrEmpty(assistantMessage.Text))
            {
                assistantMessage.SetText("（已停止生成）");
            }
        }
        catch (Exception exception)
        {
            assistantMessage.SetText($"请求失败：{exception.Message}");
            StatusText = "请求失败";
        }
        finally
        {
            _session.TextDelta -= OnTextDelta;
            _sendCts?.Dispose();
            _sendCts = null;
            IsBusy = false;
            OnPropertyChanged(nameof(Title));
        }
    }

    private async Task AppendDeltaAsync(ChatMessageViewModel message, string delta)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                message.AppendText(delta);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 窗口关闭时 Dispatcher 可能已不可用，忽略
        }
    }

    private void Clear()
    {
        Messages.Clear();
        ToolCalls.Clear();
        _session.ClearHistory();
        StatusText = "已开始新对话";
    }

    private async void OnToolCallCompleted(AiToolCallTrace trace)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                ToolCalls.Add(trace);
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 窗口关闭时 Dispatcher 可能已不可用，忽略即可
        }
    }

    private void BeginEditTitle()
    {
        EditingTitle = Title;
        IsEditingTitle = true;
    }

    private async Task CommitTitleAsync()
    {
        string title = EditingTitle?.Trim() ?? string.Empty;
        await _session.RenameAsync(title).ConfigureAwait(true);
        IsEditingTitle = false;
        OnPropertyChanged(nameof(Title));
    }

    private async void OnTitleChanged(string title)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(Title));
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 窗口关闭时忽略
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _chatClientAccessor.Changed -= OnChatClientChanged;
        _session.ToolCallCompleted -= OnToolCallCompleted;
        _session.TitleChanged -= OnTitleChanged;
    }

    private async void OnChatClientChanged(object? sender, EventArgs e)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(ActiveModelText));
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 窗口关闭时忽略
        }
    }
}


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

        _session.ToolCallCompleted += OnToolCallCompleted;

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

    /// <summary>会话标题（首条用户消息自动生成，可持久化）。</summary>
    public string Title => _session.ConversationTitle ?? "新对话";

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
        ToolCalls.Clear();
        IsBusy = true;
        StatusText = "AI 正在思考…";

        try
        {
            AiChatReply reply = await _session.SendAsync(text);
            Messages.Add(new ChatMessageViewModel(isUser: false, reply.Text));
            StatusText = reply.ToolCalls.Count == 0
                ? "完成（本轮未调用工具）"
                : $"完成（调用 {reply.ToolCalls.Count} 次工具）";
        }
        catch (Exception exception)
        {
            Messages.Add(new ChatMessageViewModel(isUser: false, $"请求失败：{exception.Message}"));
            StatusText = "请求失败";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Title));
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


using AI.Extension.Mvvm;

namespace AI.Extension.WPF.Chat;

/// <summary>聊天面板中的一条消息（文本可变，用于流式追加）。</summary>
public sealed class ChatMessageViewModel : ObservableObject
{
    private string _text;

    /// <summary>构造消息。</summary>
    public ChatMessageViewModel(bool isUser, string text)
        : this(isUser, text, DateTimeOffset.Now)
    {
    }

    /// <summary>构造消息（可指定时间，用于恢复历史会话）。</summary>
    public ChatMessageViewModel(bool isUser, string text, DateTimeOffset createdAt)
    {
        IsUser = isUser;
        _text = text ?? string.Empty;
        CreatedAt = createdAt;
    }

    /// <summary>是否用户消息。</summary>
    public bool IsUser { get; }

    /// <summary>消息正文（流式时会不断追加）。</summary>
    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    /// <summary>时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>作者显示名。</summary>
    public string Author => IsUser ? "我" : "AI 助手";

    /// <summary>时间显示文本。</summary>
    public string TimeText => CreatedAt.ToString("HH:mm:ss");

    /// <summary>追加流式增量。</summary>
    public void AppendText(string delta)
    {
        if (!string.IsNullOrEmpty(delta))
        {
            Text += delta;
        }
    }

    /// <summary>用最终文本覆盖（流式结束 / 回退非流式时使用）。</summary>
    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }
}

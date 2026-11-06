namespace AI.Extension.WPF.Chat;

/// <summary>聊天面板中的一条消息。</summary>
public sealed class ChatMessageViewModel
{
    /// <summary>构造消息。</summary>
    public ChatMessageViewModel(bool isUser, string text)
    {
        IsUser = isUser;
        Text = text ?? string.Empty;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>是否用户消息。</summary>
    public bool IsUser { get; }

    /// <summary>消息正文。</summary>
    public string Text { get; }

    /// <summary>时间。</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>作者显示名。</summary>
    public string Author => IsUser ? "我" : "AI 助手";

    /// <summary>时间显示文本。</summary>
    public string TimeText => CreatedAt.ToString("HH:mm:ss");
}

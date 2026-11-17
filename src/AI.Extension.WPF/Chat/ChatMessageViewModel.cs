using AI.Extension.Chat;
using AI.Extension.Mvvm;

namespace AI.Extension.WPF.Chat;

/// <summary>聊天面板中的一条消息（文本可变，用于流式追加；也承载 ask_user 选择卡片）。</summary>
public sealed class ChatMessageViewModel : ObservableObject
{
    private readonly Action<AiAskUserResult>? _onChoice;
    private string _text;
    private string _choiceFreeText = string.Empty;
    private bool _isChoiceResolved;
    private string _choiceResultText = string.Empty;

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
        SelectChoiceCommand = new RelayCommand(parameter => SelectChoice(parameter as string));
        SubmitChoiceCommand = new RelayCommand(_ => SubmitChoice());
    }

    private ChatMessageViewModel(AiAskUserRequest request, Action<AiAskUserResult> onChoice)
        : this(isUser: false, text: request.Question)
    {
        IsChoiceRequest = true;
        ChoiceQuestion = request.Question;
        ChoiceOptions = request.Options;
        AllowChoiceFreeText = request.AllowFreeText;
        _onChoice = onChoice;
    }

    /// <summary>创建 ask_user 选择卡片。</summary>
    public static ChatMessageViewModel CreateChoice(
        AiAskUserRequest request,
        Action<AiAskUserResult> onChoice)
        => new(request, onChoice);

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

    /// <summary>是否为用户选择卡片。</summary>
    public bool IsChoiceRequest { get; }

    /// <summary>问题文本。</summary>
    public string ChoiceQuestion { get; } = string.Empty;

    /// <summary>候选项。</summary>
    public IReadOnlyList<string> ChoiceOptions { get; } = Array.Empty<string>();

    /// <summary>是否允许自由输入。</summary>
    public bool AllowChoiceFreeText { get; }

    /// <summary>自由输入文本。</summary>
    public string ChoiceFreeText
    {
        get => _choiceFreeText;
        set => SetProperty(ref _choiceFreeText, value);
    }

    /// <summary>是否已经完成选择。</summary>
    public bool IsChoiceResolved
    {
        get => _isChoiceResolved;
        private set => SetProperty(ref _isChoiceResolved, value);
    }

    /// <summary>选择结果展示文本。</summary>
    public string ChoiceResultText
    {
        get => _choiceResultText;
        private set => SetProperty(ref _choiceResultText, value);
    }

    /// <summary>选择某个选项。</summary>
    public RelayCommand SelectChoiceCommand { get; }

    /// <summary>提交自由输入。</summary>
    public RelayCommand SubmitChoiceCommand { get; }

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

    /// <summary>标记为已取消（停止生成时调用）。</summary>
    public void CancelChoice() => Resolve(new AiAskUserResult(null, null, Cancelled: true));

    private void SelectChoice(string? option)
    {
        if (string.IsNullOrWhiteSpace(option))
        {
            return;
        }

        Resolve(new AiAskUserResult(option, null, Cancelled: false));
    }

    private void SubmitChoice()
    {
        string text = ChoiceFreeText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Resolve(new AiAskUserResult(null, text, Cancelled: false));
    }

    private void Resolve(AiAskUserResult result)
    {
        if (IsChoiceResolved)
        {
            return;
        }

        IsChoiceResolved = true;
        ChoiceResultText = result.ToDisplayText();
        _onChoice?.Invoke(result);
    }
}

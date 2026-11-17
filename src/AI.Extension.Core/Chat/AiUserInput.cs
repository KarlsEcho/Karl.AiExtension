namespace AI.Extension.Chat;

/// <summary>AI 通过内置 <c>ask_user</c> 工具向用户发起的交互请求。</summary>
/// <param name="Question">问题文本。</param>
/// <param name="Options">候选项（可能为空，表示只允许自由输入）。</param>
/// <param name="AllowFreeText">是否允许用户自由输入。</param>
public sealed record AiAskUserRequest(
    string Question,
    IReadOnlyList<string> Options,
    bool AllowFreeText);

/// <summary>用户对 <see cref="AiAskUserRequest"/> 的答复。</summary>
/// <param name="SelectedOption">用户选择的具体选项。</param>
/// <param name="FreeText">用户自由输入的文本。</param>
/// <param name="Cancelled">用户是否取消。</param>
/// <param name="Unavailable">当前宿主是否不具备交互能力。</param>
public sealed record AiAskUserResult(
    string? SelectedOption,
    string? FreeText,
    bool Cancelled,
    bool Unavailable = false)
{
    /// <summary>宿主没有提供交互 UI 时的结果。</summary>
    public static readonly AiAskUserResult UnavailableResult = new(null, null, false, Unavailable: true);

    /// <summary>转换为回灌给模型的可读文本。</summary>
    public string ToDisplayText()
    {
        if (Unavailable)
        {
            return "当前宿主不支持用户交互（ask_user），请根据已有信息继续，或直接向用户说明需要补充信息。";
        }

        if (Cancelled)
        {
            return "用户取消了选择。";
        }

        if (!string.IsNullOrWhiteSpace(SelectedOption))
        {
            return $"用户选择：{SelectedOption.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(FreeText))
        {
            return $"用户输入：{FreeText.Trim()}";
        }

        return "用户未提供选择。";
    }
}

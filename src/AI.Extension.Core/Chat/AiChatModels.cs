namespace AI.Extension.Chat;

/// <summary>一次工具调用的轨迹，供 UI 实时展示「AI 做了什么」。</summary>
/// <param name="ToolName">MCP 工具名。</param>
/// <param name="ArgumentsJson">入参 JSON。</param>
/// <param name="Success">工具是否成功。</param>
/// <param name="ResultPreview">结果预览（截断）。</param>
/// <param name="Duration">耗时。</param>
public sealed record AiToolCallTrace(
    string ToolName,
    string ArgumentsJson,
    bool Success,
    string ResultPreview,
    TimeSpan Duration);

/// <summary>一轮对话的回复。</summary>
/// <param name="Text">助手最终文本。</param>
/// <param name="ToolCalls">本轮发生的工具调用。</param>
public sealed record AiChatReply(string Text, IReadOnlyList<AiToolCallTrace> ToolCalls)
{
    /// <summary>空回复。</summary>
    public static readonly AiChatReply Empty = new(string.Empty, Array.Empty<AiToolCallTrace>());
}

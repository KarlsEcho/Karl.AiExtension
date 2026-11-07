namespace AI.Extension.Chat;

/// <summary>
/// AI 对话行为配置（系统提示、上下文注入条数、工具轮次上限）。
/// <para>
/// 与「供应商/模型」配置分离：供应商、Endpoint、API Key、模型列表由
/// <see cref="Providers.IAiProviderManager"/> 管理并持久化，可在运行时切换。
/// </para>
/// </summary>
public sealed class AiChatOptions
{
    /// <summary>默认系统提示词。</summary>
    public const string DefaultSystemPrompt = """
        你是运行在桌面应用内的 AI 助手。
        你可以调用 MCP 工具操作应用中的业务数据（订单等），工具调用会在界面上实时同步。
        规则：
        1. 需要查询/修改数据时优先调用工具，不要凭空编造业务数据；
        2. 工具返回失败时，先根据错误原因自我纠正（例如修正参数）再重试，最多重试一次；
        3. 涉及删除、取消等破坏性操作时，先向用户确认意图；
        4. 回答使用简体中文，简洁、给出关键数据，不要输出工具调用的原始 JSON。
        """;

    /// <summary>系统提示词。</summary>
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;

    /// <summary>每次对话注入的 UI 活动条数。</summary>
    public int UiContextEntryCount { get; set; } = 10;

    /// <summary>单个请求里允许的最大工具调用轮次，防止模型死循环。</summary>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>首轮对话结束后是否让 AI 生成会话标题（失败时回退为首条用户消息截断）。</summary>
    public bool AutoTitleFromFirstRound { get; set; } = true;

    /// <summary>AI 生成标题的最大字数。</summary>
    public int AutoTitleMaxLength { get; set; } = 20;
}

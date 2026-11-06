namespace AI.Extension.Chat;

/// <summary>AI 对话配置。支持从环境变量读取，便于 WPF 与 Blazor 共用同一份配置。</summary>
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

    /// <summary>OpenAI 兼容端点，例如 <c>https://api.deepseek.com/v1</c>。</summary>
    public string? Endpoint { get; set; }

    /// <summary>API Key。</summary>
    public string? ApiKey { get; set; }

    /// <summary>模型名，例如 <c>deepseek-chat</c>。</summary>
    public string? Model { get; set; }

    /// <summary>每次对话注入的 UI 活动条数。</summary>
    public int UiContextEntryCount { get; set; } = 10;

    /// <summary>单个请求里允许的最大工具调用轮次，防止模型死循环。</summary>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>是否已配置远程模型。</summary>
    public bool IsRemoteConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model);

    /// <summary>从环境变量构造配置。</summary>
    public static AiChatOptions FromEnvironment()
    {
        var options = new AiChatOptions
        {
            Endpoint = FirstNonEmpty(
                Environment.GetEnvironmentVariable("KARL_AI_ENDPOINT"),
                Environment.GetEnvironmentVariable("OPENAI_BASE_URL")),
            ApiKey = FirstNonEmpty(
                Environment.GetEnvironmentVariable("KARL_AI_API_KEY"),
                Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
            Model = FirstNonEmpty(
                Environment.GetEnvironmentVariable("KARL_AI_MODEL"),
                Environment.GetEnvironmentVariable("OPENAI_MODEL")),
        };

        if (int.TryParse(Environment.GetEnvironmentVariable("KARL_AI_MAX_TOOL_ITERATIONS"), out int iterations) && iterations > 0)
        {
            options.MaxToolIterations = iterations;
        }

        return options;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

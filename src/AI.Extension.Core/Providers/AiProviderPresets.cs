namespace AI.Extension.Providers;

/// <summary>
/// 内置供应商预设：首次运行时写入配置文件，用户只需填 API Key。
/// 预设默认都是「禁用」状态，避免未配置密钥的供应商被误选。
/// </summary>
public static class AiProviderPresets
{
    /// <summary>
    /// 内置预设的版本号。递增后，管理器会在启动时把「旧安装里的内置预设」迁移到新参数
    /// （保留用户填写的 API Key 与启用状态），避免用户机器上残留过期的端点/模型。
    /// </summary>
    public const int SchemaVersion = 2;

    /// <summary>内置供应商列表。</summary>
    public static IReadOnlyList<AiProviderDefinition> CreateDefaults() =>
    [
        new AiProviderDefinition
        {
            Id = "deepseek",
            DisplayName = "DeepSeek",
            // 官方 2026 文档：OpenAI 格式 base_url = https://api.deepseek.com（不带 /v1）
            Endpoint = "https://api.deepseek.com",
            Enabled = false,
            DefaultModelId = "deepseek-flash",
            Models =
            [
                new AiModelDefinition { Id = "deepseek-flash", DisplayName = "DeepSeek V4.1 Flash" },
                new AiModelDefinition { Id = "deepseek-v4-pro", DisplayName = "DeepSeek V4 Pro" },
            ],
            Notes = "OpenAI 兼容端点 https://api.deepseek.com；模型 deepseek-flash / deepseek-v4-pro（旧名 deepseek-chat 已下线）",
        },
        new AiProviderDefinition
        {
            Id = "openai",
            DisplayName = "OpenAI",
            Endpoint = "https://api.openai.com/v1",
            Enabled = false,
            DefaultModelId = "gpt-4o-mini",
            Models =
            [
                new AiModelDefinition { Id = "gpt-4o-mini" },
                new AiModelDefinition { Id = "gpt-4o" },
            ],
            Notes = "官方端点；可换成任意兼容代理",
        },
        new AiProviderDefinition
        {
            Id = "dashscope",
            DisplayName = "阿里云百炼（通义）",
            Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Enabled = false,
            DefaultModelId = "qwen-plus",
            Models =
            [
                new AiModelDefinition { Id = "qwen-plus" },
                new AiModelDefinition { Id = "qwen-max" },
                new AiModelDefinition { Id = "qwen-turbo" },
            ],
            Notes = "百炼的 OpenAI 兼容模式",
        },
        new AiProviderDefinition
        {
            Id = "ollama",
            DisplayName = "本地 Ollama",
            Endpoint = "http://localhost:11434/v1",
            Enabled = false,
            RequiresApiKey = false,
            DefaultModelId = "qwen2.5:7b",
            Models =
            [
                new AiModelDefinition { Id = "qwen2.5:7b", DisplayName = "Qwen2.5 7B" },
                new AiModelDefinition { Id = "llama3.1:8b", DisplayName = "Llama 3.1 8B" },
            ],
            Notes = "本地推理，无需 API Key；先 ollama pull 对应模型",
        },
    ];

    /// <summary>
    /// 从环境变量生成一个供应商（便于 CI / 容器部署时免配置文件）：
    /// <c>KARL_AI_ENDPOINT / KARL_AI_API_KEY / KARL_AI_MODEL</c>，
    /// 兼容 <c>OPENAI_BASE_URL / OPENAI_API_KEY / OPENAI_MODEL</c>。
    /// </summary>
    public static AiProviderDefinition? TryCreateFromEnvironment()
    {
        string? endpoint = FirstNonEmpty(
            Environment.GetEnvironmentVariable("KARL_AI_ENDPOINT"),
            Environment.GetEnvironmentVariable("OPENAI_BASE_URL"));
        string? apiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("KARL_AI_API_KEY"),
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        string? model = FirstNonEmpty(
            Environment.GetEnvironmentVariable("KARL_AI_MODEL"),
            Environment.GetEnvironmentVariable("OPENAI_MODEL"));

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        return new AiProviderDefinition
        {
            Id = "environment",
            DisplayName = "环境变量导入",
            Endpoint = endpoint!,
            ApiKey = apiKey,
            Enabled = true,
            DefaultModelId = model,
            Models = [new AiModelDefinition { Id = model! }],
            Notes = "由 KARL_AI_* / OPENAI_* 环境变量生成",
        };
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

namespace AI.Extension.Providers;

/// <summary>AI 供应商的协议类型。</summary>
public enum AiProviderKind
{
    /// <summary>OpenAI 兼容协议（DeepSeek、OpenAI、通义兼容模式、Ollama、vLLM 等）。</summary>
    OpenAICompatible = 0,
}

/// <summary>一个可选的模型。</summary>
public sealed record AiModelDefinition
{
    /// <summary>模型 Id（请求里使用的名字）。</summary>
    public required string Id { get; init; }

    /// <summary>显示名，为空时展示 <see cref="Id"/>。</summary>
    public string? DisplayName { get; init; }

    /// <summary>下拉框显示文本。</summary>
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!;
}

/// <summary>一个 AI 供应商配置。</summary>
public sealed record AiProviderDefinition
{
    /// <summary>稳定标识（同一份配置里唯一，例如 <c>deepseek</c>）。</summary>
    public required string Id { get; init; }

    /// <summary>显示名，例如「DeepSeek」。</summary>
    public required string DisplayName { get; init; }

    /// <summary>协议类型。</summary>
    public AiProviderKind Kind { get; init; } = AiProviderKind.OpenAICompatible;

    /// <summary>OpenAI 兼容端点，例如 <c>https://api.deepseek.com/v1</c>。</summary>
    public required string Endpoint { get; init; }

    /// <summary>API Key（内存中为明文；持久化时由 <c>IAiSecretProtector</c> 加密）。</summary>
    public string? ApiKey { get; init; }

    /// <summary>是否启用（禁用后不会出现在可切换列表里）。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>是否必须提供 API Key（本地 Ollama 之类的供应商可设为 false）。</summary>
    public bool RequiresApiKey { get; init; } = true;

    /// <summary>该供应商下的模型列表。</summary>
    public IReadOnlyList<AiModelDefinition> Models { get; init; } = Array.Empty<AiModelDefinition>();

    /// <summary>默认模型 Id。</summary>
    public string? DefaultModelId { get; init; }

    /// <summary>备注（例如申请地址、额度提示）。</summary>
    public string? Notes { get; init; }
}

/// <summary>持久化的供应商状态：供应商列表 + 当前选中的供应商/模型。</summary>
public sealed record AiProviderState(
    IReadOnlyList<AiProviderDefinition> Providers,
    string? ActiveProviderId,
    string? ActiveModelId)
{
    /// <summary>空状态。</summary>
    public static readonly AiProviderState Empty = new(Array.Empty<AiProviderDefinition>(), null, null);
}

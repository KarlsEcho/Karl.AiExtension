namespace AI.Extension.Providers;

/// <summary>供应商配置的持久化抽象。</summary>
/// <remarks>
/// 默认实现把文件放在 <c>%APPDATA%\Karl.AiExtension\providers.json</c>，
/// 其中 API Key 经过平台凭据保护（Windows 用 DPAPI，其他平台降级为混淆并给出警告）。
/// </remarks>
public interface IAiProviderStore
{
    /// <summary>加载供应商状态；文件不存在或损坏时返回 <see cref="AiProviderState.Empty"/>。</summary>
    Task<AiProviderState> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>原子保存供应商状态。</summary>
    Task SaveAsync(AiProviderState state, CancellationToken cancellationToken = default);
}

/// <summary>运行时供应商/模型管理。</summary>
public interface IAiProviderManager
{
    /// <summary>当前所有供应商（快照）。</summary>
    IReadOnlyList<AiProviderDefinition> Providers { get; }

    /// <summary>当前选中的供应商。</summary>
    AiProviderDefinition? ActiveProvider { get; }

    /// <summary>当前选中的模型。</summary>
    AiModelDefinition? ActiveModel { get; }

    /// <summary>是否已有可用的当前模型。</summary>
    bool HasActiveModel { get; }

    /// <summary>当前模型的展示文本，例如「DeepSeek · deepseek-chat」。</summary>
    string ActiveModelLabel { get; }

    /// <summary>供应商/当前模型发生变化。</summary>
    event EventHandler? Changed;

    /// <summary>加载持久化配置并在首次运行时写入默认供应商。幂等。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>按 Id 查找供应商。</summary>
    AiProviderDefinition? Find(string providerId);

    /// <summary>新增或更新供应商（保存后立即持久化）。</summary>
    Task<AiProviderDefinition> SaveAsync(AiProviderDefinition provider, CancellationToken cancellationToken = default);

    /// <summary>删除供应商；若删除的是当前供应商，会自动切换到下一个可用供应商。</summary>
    Task RemoveAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>设置当前供应商与模型；<paramref name="providerId"/> 为 null 表示回到「未配置」。</summary>
    Task SetActiveAsync(string? providerId, string? modelId, CancellationToken cancellationToken = default);

    /// <summary>测试连通性（请求 <c>{endpoint}/models</c>）。</summary>
    Task<bool> TestConnectionAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>从供应商端点拉取模型列表。</summary>
    Task<IReadOnlyList<string>> DiscoverModelsAsync(string providerId, CancellationToken cancellationToken = default);
}

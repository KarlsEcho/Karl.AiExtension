using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AI.Extension.Providers;

/// <summary>
/// 供应商/模型管理的默认实现：内存状态 + 持久化存储 + 运行时可切换。
/// <para>
/// 首次 <see cref="InitializeAsync"/> 时若没有持久化配置，会写入内置预设
/// （以及环境变量导入的供应商），之后所有修改都会立即写回存储。
/// </para>
/// </summary>
public sealed class AiProviderManager : IAiProviderManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IAiProviderStore _store;
    private readonly ILogger<AiProviderManager>? _logger;
    private readonly IReadOnlyList<AiProviderDefinition> _seedProviders;
    private readonly List<AiProviderDefinition> _providers = new();

    private string? _activeProviderId;
    private string? _activeModelId;
    private bool _initialized;

    /// <summary>构造管理器。</summary>
    /// <param name="store">持久化存储。</param>
    /// <param name="logger">可选日志。</param>
    /// <param name="seedProviders">首次运行写入的预设；为空时使用 <see cref="AiProviderPresets"/>。</param>
    public AiProviderManager(
        IAiProviderStore store,
        ILogger<AiProviderManager>? logger = null,
        IReadOnlyList<AiProviderDefinition>? seedProviders = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger;
        _seedProviders = seedProviders ?? BuildDefaultSeeds();
    }

    /// <inheritdoc />
    public IReadOnlyList<AiProviderDefinition> Providers
    {
        get
        {
            lock (_sync)
            {
                return _providers.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public AiProviderDefinition? ActiveProvider
    {
        get
        {
            lock (_sync)
            {
                return _activeProviderId is null
                    ? null
                    : _providers.FirstOrDefault(provider => provider.Id == _activeProviderId);
            }
        }
    }

    /// <inheritdoc />
    public AiModelDefinition? ActiveModel
    {
        get
        {
            lock (_sync)
            {
                AiProviderDefinition? provider = _activeProviderId is null
                    ? null
                    : _providers.FirstOrDefault(candidate => candidate.Id == _activeProviderId);

                if (provider is null || _activeModelId is null)
                {
                    return null;
                }

                return provider.Models.FirstOrDefault(model => model.Id == _activeModelId);
            }
        }
    }

    /// <inheritdoc />
    public bool HasActiveModel => ActiveProvider is not null && ActiveModel is not null;

    /// <inheritdoc />
    public string ActiveModelLabel
    {
        get
        {
            AiProviderDefinition? provider = ActiveProvider;
            AiModelDefinition? model = ActiveModel;
            return provider is null || model is null ? "未配置（离线演示）" : $"{provider.DisplayName} · {model.Id}";
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            AiProviderState state;
            try
            {
                state = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "读取供应商配置失败，使用空配置启动。");
                state = AiProviderState.Empty;
            }

            lock (_sync)
            {
                _providers.Clear();
                _providers.AddRange(state.Providers);
                _activeProviderId = state.ActiveProviderId;
                _activeModelId = state.ActiveModelId;

                if (_providers.Count == 0)
                {
                    foreach (AiProviderDefinition seed in _seedProviders)
                    {
                        _providers.Add(seed);
                    }

                    _activeProviderId = null;
                    _activeModelId = null;
                }

                EnsureActiveConsistentLocked();
                _initialized = true;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("供应商配置已加载：{Count} 个供应商，当前模型 {Model}。", _providers.Count, ActiveModelLabel);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public AiProviderDefinition? Find(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        lock (_sync)
        {
            return _providers.FirstOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal));
        }
    }

    /// <inheritdoc />
    public async Task<AiProviderDefinition> SaveAsync(
        AiProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        AiProviderDefinition normalized = Normalize(provider);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                EnsureInitializedLocked();

                int index = _providers.FindIndex(candidate => string.Equals(candidate.Id, normalized.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _providers[index] = normalized;
                }
                else
                {
                    _providers.Add(normalized);
                }

                EnsureActiveConsistentLocked();
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
        return normalized;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string providerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                EnsureInitializedLocked();

                int removed = _providers.RemoveAll(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal));
                if (removed == 0)
                {
                    throw new ArgumentException($"供应商 {providerId} 不存在。", nameof(providerId));
                }

                EnsureActiveConsistentLocked();
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(
        string? providerId,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                EnsureInitializedLocked();

                if (string.IsNullOrWhiteSpace(providerId))
                {
                    // 显式清除：不自动挑选下一个可用供应商（那是「删除/禁用」时的补偿逻辑）
                    _activeProviderId = null;
                    _activeModelId = null;
                }
                else
                {
                    AiProviderDefinition provider = _providers.FirstOrDefault(candidate => candidate.Id == providerId)
                        ?? throw new ArgumentException($"供应商 {providerId} 不存在。", nameof(providerId));

                    if (!provider.Enabled)
                    {
                        throw new InvalidOperationException($"供应商「{provider.DisplayName}」未启用，请先保存并启用。");
                    }

                    AiModelDefinition? model = PickModel(provider, modelId)
                        ?? throw new InvalidOperationException($"供应商「{provider.DisplayName}」没有可用模型。");

                    _activeProviderId = provider.Id;
                    _activeModelId = model.Id;
                }
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        RaiseChanged();
    }

    /// <inheritdoc />
    public async Task<bool> TestConnectionAsync(string providerId, CancellationToken cancellationToken = default)
    {
        try
        {
            await DiscoverModelsAsync(providerId, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "测试供应商 {ProviderId} 连接失败。", providerId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DiscoverModelsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        AiProviderDefinition provider = Find(providerId)
            ?? throw new ArgumentException($"供应商 {providerId} 不存在。", nameof(providerId));

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUrl(provider));
        if (!string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", provider.ApiKey);
        }

        using HttpResponseMessage response = await Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var models = new List<string>();
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("data", out JsonElement data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("id", out JsonElement id) &&
                    id.ValueKind == JsonValueKind.String)
                {
                    string? modelId = id.GetString();
                    if (!string.IsNullOrWhiteSpace(modelId))
                    {
                        models.Add(modelId!);
                    }
                }
            }
        }

        return models;
    }

    private static string BuildModelsUrl(AiProviderDefinition provider)
        => provider.Endpoint.TrimEnd('/') + "/models";

    private void EnsureInitializedLocked()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("供应商管理器尚未初始化，请先调用 InitializeAsync()。");
        }
    }

    private void EnsureActiveConsistentLocked()
    {
        AiProviderDefinition? active = _activeProviderId is null
            ? null
            : _providers.FirstOrDefault(provider => provider.Id == _activeProviderId);

        if (active is null || !active.Enabled || active.Models.Count == 0)
        {
            _activeProviderId = null;
            _activeModelId = null;
        }
        else
        {
            AiModelDefinition? model = PickModel(active, _activeModelId);
            if (model is null)
            {
                _activeProviderId = null;
                _activeModelId = null;
            }
            else
            {
                _activeModelId = model.Id;
            }
        }

        if (_activeProviderId is not null)
        {
            return;
        }

        AiProviderDefinition? fallback = _providers.FirstOrDefault(provider => provider.Enabled && provider.Models.Count > 0);
        if (fallback is not null)
        {
            _activeProviderId = fallback.Id;
            _activeModelId = PickModel(fallback, null)?.Id;
        }
    }

    private static AiModelDefinition? PickModel(AiProviderDefinition provider, string? preferredModelId)
    {
        if (!string.IsNullOrWhiteSpace(preferredModelId))
        {
            AiModelDefinition? preferred = provider.Models
                .FirstOrDefault(model => string.Equals(model.Id, preferredModelId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        if (!string.IsNullOrWhiteSpace(provider.DefaultModelId))
        {
            AiModelDefinition? defaultModel = provider.Models
                .FirstOrDefault(model => string.Equals(model.Id, provider.DefaultModelId, StringComparison.OrdinalIgnoreCase));
            if (defaultModel is not null)
            {
                return defaultModel;
            }
        }

        return provider.Models.FirstOrDefault();
    }

    private static AiProviderDefinition Normalize(AiProviderDefinition provider)
    {
        string id = provider.Id?.Trim() ?? string.Empty;
        if (id.Length == 0)
        {
            throw new ArgumentException("供应商 Id 不能为空。", nameof(provider));
        }

        if (id.Length > 64)
        {
            throw new ArgumentException("供应商 Id 不能超过 64 个字符。", nameof(provider));
        }

        string displayName = provider.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0)
        {
            throw new ArgumentException("供应商名称不能为空。", nameof(provider));
        }

        string endpoint = (provider.Endpoint ?? string.Empty).Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Endpoint 必须是合法的 http/https 地址，例如 https://api.deepseek.com/v1。", nameof(provider));
        }

        AiModelDefinition[] models = (provider.Models ?? Array.Empty<AiModelDefinition>())
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new AiModelDefinition
            {
                Id = model.Id.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName!.Trim(),
            })
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (models.Length == 0)
        {
            throw new ArgumentException("请至少配置一个模型。", nameof(provider));
        }

        string? defaultModelId = provider.DefaultModelId?.Trim();
        if (string.IsNullOrWhiteSpace(defaultModelId) ||
            models.All(model => !string.Equals(model.Id, defaultModelId, StringComparison.OrdinalIgnoreCase)))
        {
            defaultModelId = models[0].Id;
        }

        string? apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? null : provider.ApiKey.Trim();
        if (provider.Enabled && provider.RequiresApiKey && apiKey is null)
        {
            throw new ArgumentException("启用该供应商前请填写 API Key。", nameof(provider));
        }

        return provider with
        {
            Id = id,
            DisplayName = displayName,
            Endpoint = endpoint,
            ApiKey = apiKey,
            Models = models,
            DefaultModelId = defaultModelId,
        };
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        AiProviderState state;
        lock (_sync)
        {
            state = new AiProviderState(_providers.ToArray(), _activeProviderId, _activeModelId);
        }

        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static IReadOnlyList<AiProviderDefinition> BuildDefaultSeeds()
    {
        var seeds = new List<AiProviderDefinition>(AiProviderPresets.CreateDefaults());

        AiProviderDefinition? fromEnvironment = AiProviderPresets.TryCreateFromEnvironment();
        if (fromEnvironment is not null)
        {
            seeds.Insert(0, fromEnvironment);
        }

        return seeds;
    }
}

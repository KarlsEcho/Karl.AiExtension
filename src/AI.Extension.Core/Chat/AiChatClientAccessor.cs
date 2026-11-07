using AI.Extension.Providers;
using Microsoft.Extensions.AI;

namespace AI.Extension.Chat;

/// <summary>
/// 取「当前生效的聊天客户端」：有激活的供应商/模型时按配置创建，否则返回兜底客户端。
/// 供应商切换后会自动重建客户端并触发 <see cref="Changed"/>，UI 可据此刷新「当前模型」显示。
/// </summary>
public interface IAiChatClientAccessor
{
    /// <summary>当前生效的聊天客户端。</summary>
    IChatClient Current { get; }

    /// <summary>当前模型展示文本，例如「DeepSeek · deepseek-chat」或「离线演示模式」。</summary>
    string DisplayName { get; }

    /// <summary>是否已配置真实供应商/模型（未配置时不应让兜底客户端参与标题生成等附加请求）。</summary>
    bool IsConfigured => true;

    /// <summary>当前模型发生变化。</summary>
    event EventHandler? Changed;
}

/// <summary><see cref="IAiChatClientAccessor"/> 的默认实现。</summary>
public sealed class AiChatClientAccessor : IAiChatClientAccessor, IDisposable
{
    private readonly IAiProviderManager _providers;
    private readonly IChatClient _fallback;
    private readonly object _sync = new();

    private IChatClient? _current;
    private string? _currentKey;
    private string _displayName = "离线演示模式（未配置供应商）";
    private bool _disposed;

    /// <summary>构造访问器。</summary>
    public AiChatClientAccessor(IAiProviderManager providers, IChatClient fallback)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _providers.Changed += OnProvidersChanged;
    }

    /// <inheritdoc />
    public IChatClient Current
    {
        get
        {
            lock (_sync)
            {
                return ResolveLocked();
            }
        }
    }

    /// <inheritdoc />
    public string DisplayName
    {
        get
        {
            lock (_sync)
            {
                ResolveLocked();
                return _displayName;
            }
        }
    }

    /// <inheritdoc />
    public bool IsConfigured
        => _providers.ActiveProvider is not null && _providers.ActiveModel is not null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _providers.Changed -= OnProvidersChanged;

        lock (_sync)
        {
            DisposeCurrentLocked();
        }
    }

    private void OnProvidersChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            DisposeCurrentLocked();
            ResolveLocked();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IChatClient ResolveLocked()
    {
        if (_current is not null)
        {
            return _current;
        }

        AiProviderDefinition? provider = _providers.ActiveProvider;
        AiModelDefinition? model = _providers.ActiveModel;

        if (provider is null || model is null)
        {
            _displayName = "离线演示模式（未配置供应商）";
            return _fallback;
        }

        string key = provider.Id + "::" + model.Id;
        if (_current is not null && string.Equals(_currentKey, key, StringComparison.Ordinal))
        {
            return _current;
        }

        try
        {
            _current = AiProviderClientFactory.Create(provider, model);
            _currentKey = key;
            _displayName = $"{provider.DisplayName} · {model.Id}";
            return _current;
        }
        catch (Exception exception)
        {
            _current = null;
            _currentKey = null;
            _displayName = "供应商配置无效：" + exception.Message;
            return _fallback;
        }
    }

    private void DisposeCurrentLocked()
    {
        if (_current is null)
        {
            return;
        }

        (_current as IDisposable)?.Dispose();
        _current = null;
        _currentKey = null;
    }
}


namespace AI.Extension.Providers;

/// <summary>内存实现：用于测试、设计时与绿色演示（不落盘）。</summary>
public sealed class InMemoryAiProviderStore : IAiProviderStore
{
    private readonly object _sync = new();
    private AiProviderState _state = AiProviderState.Empty;

    /// <summary>用初始状态构造。</summary>
    public InMemoryAiProviderStore(AiProviderState? initialState = null)
        => _state = initialState ?? AiProviderState.Empty;

    /// <summary>当前保存的状态（测试断言用）。</summary>
    public AiProviderState CurrentState
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public Task<AiProviderState> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            return Task.FromResult(_state);
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(AiProviderState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _state = state;
        }

        return Task.CompletedTask;
    }
}

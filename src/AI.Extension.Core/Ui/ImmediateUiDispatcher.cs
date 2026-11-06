namespace AI.Extension;

/// <summary>
/// 无 UI 线程的实现：直接在当前线程执行。
/// 用于控制台宿主、单元测试，以及不关心线程亲和性的场景。
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<Task<T>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return callback();
    }

    /// <inheritdoc />
    public Task InvokeAsync(Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return callback();
    }
}

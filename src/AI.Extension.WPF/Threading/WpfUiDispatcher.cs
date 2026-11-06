using System.Windows;
using System.Windows.Threading;

namespace AI.Extension.WPF.Threading;

/// <summary>
/// WPF 的 UI 线程调度实现。
/// <para>
/// AI 工具调用在 MCP 管道的后台线程上执行，而 ViewModel 的集合绑定在 WPF 的 Dispatcher 上。
/// 核心层只依赖 <see cref="IUiDispatcher"/>，因此同一套 ViewModel 可以同时被 WPF 与 Blazor 复用。
/// </para>
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<Task<T>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (Dispatcher.CheckAccess())
        {
            return callback();
        }

        return Dispatcher.InvokeAsync(callback).Task.Unwrap();
    }

    /// <inheritdoc />
    public Task InvokeAsync(Func<Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (Dispatcher.CheckAccess())
        {
            return callback();
        }

        return Dispatcher.InvokeAsync(callback).Task.Unwrap();
    }
}

namespace AI.Extension;

/// <summary>
/// UI 线程调度抽象。
/// <para>
/// AI 工具调用发生在后台线程，而 ViewModel 的集合/属性最终绑定到 WPF 或 Blazor 的 UI 线程。
/// 核心层必须依赖这个抽象而不是 WPF 的 <c>Dispatcher</c>，
/// 否则 <c>AI.Extension.Core</c> 就无法被 Blazor 复用（这正是架构里强调的线程亲和性问题）。
/// </para>
/// </summary>
public interface IUiDispatcher
{
    /// <summary>把有返回值的操作调度到 UI 线程执行。</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> callback);

    /// <summary>把无返回值的操作调度到 UI 线程执行。</summary>
    Task InvokeAsync(Func<Task> callback);
}

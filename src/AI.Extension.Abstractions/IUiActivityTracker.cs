namespace AI.Extension;

/// <summary>UI 活动类别：用于把「用户/界面意图」结构化地喂给 AI。</summary>
public enum UiActivityCategory
{
    /// <summary>导航（切换页面/视图）。</summary>
    Navigation,

    /// <summary>选择（选中行、焦点、勾选）。</summary>
    Selection,

    /// <summary>命令（用户点击了某个动作/按钮）。</summary>
    Command,

    /// <summary>数据变更（新增、修改、删除）。</summary>
    DataChange,

    /// <summary>AI 工具调用（由 MCP 封装层记录）。</summary>
    AgentAction,

    /// <summary>其他。</summary>
    Other,
}

/// <summary>一条语义化的 UI 活动记录。</summary>
/// <param name="Timestamp">发生时间。</param>
/// <param name="Category">类别。</param>
/// <param name="Description">人可读的意图描述，例如「切换到订单页」「选中订单 SO-1002」。</param>
/// <param name="Detail">可选细节（JSON 片段、数值等）。</param>
public sealed record UiActivityEntry(
    DateTimeOffset Timestamp,
    UiActivityCategory Category,
    string Description,
    string? Detail = null);

/// <summary>
/// UI 上下文追踪器（环形日志）。
/// <para>
/// 架构决策：<b>不主动把 UI 事件推给 AI</b>，而是静默记录语义化活动；
/// 当用户唤起 AI 时，再按需读取最近 N 条日志作为隐藏上下文注入。
/// 这样既避免 Token 爆炸与噪音干扰，也避免 UI 线程被高频推送拖垮。
/// </para>
/// </summary>
public interface IUiActivityTracker
{
    /// <summary>记录一条语义化活动（实现必须线程安全，且不得阻塞 UI 线程）。</summary>
    void Record(UiActivityCategory category, string description, string? detail = null);

    /// <summary>读取最近的活动快照（按时间正序返回，最多 <paramref name="maxCount"/> 条）。</summary>
    IReadOnlyList<UiActivityEntry> Snapshot(int maxCount = 20);

    /// <summary>把最近活动渲染成适合注入 Prompt 的精简文本。</summary>
    string BuildContextSummary(int maxCount = 12);
}

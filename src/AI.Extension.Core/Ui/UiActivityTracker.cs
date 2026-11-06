using System.Text;

namespace AI.Extension;

/// <summary>
/// UI 上下文追踪器的默认实现：固定容量的环形日志。
/// <para>
/// 线程安全、无分配热点、不阻塞调用方；只记录「语义化」活动，而不是原始 UI 事件。
/// </para>
/// </summary>
public sealed class UiActivityTracker : IUiActivityTracker
{
    private readonly object _gate = new();
    private readonly Queue<UiActivityEntry> _entries;
    private readonly int _capacity;

    /// <summary>创建追踪器。</summary>
    /// <param name="capacity">环形缓冲区容量，默认 200 条。</param>
    public UiActivityTracker(int capacity = 200)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于 0。");
        }

        _capacity = capacity;
        _entries = new Queue<UiActivityEntry>(capacity);
    }

    /// <inheritdoc />
    public void Record(UiActivityCategory category, string description, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        UiActivityEntry entry = new(DateTimeOffset.Now, category, description, detail);

        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UiActivityEntry> Snapshot(int maxCount = 20)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<UiActivityEntry>();
        }

        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return Array.Empty<UiActivityEntry>();
            }

            int skip = Math.Max(0, _entries.Count - maxCount);
            return _entries.Skip(skip).ToArray();
        }
    }

    /// <inheritdoc />
    public string BuildContextSummary(int maxCount = 12)
    {
        IReadOnlyList<UiActivityEntry> snapshot = Snapshot(maxCount);
        if (snapshot.Count == 0)
        {
            return "（暂无界面活动记录）";
        }

        var builder = new StringBuilder();
        foreach (UiActivityEntry entry in snapshot)
        {
            builder.Append("- [")
                .Append(entry.Timestamp.ToString("HH:mm:ss"))
                .Append("] ")
                .Append(CategoryLabel(entry.Category))
                .Append("：")
                .Append(entry.Description);

            if (!string.IsNullOrWhiteSpace(entry.Detail))
            {
                builder.Append("（").Append(entry.Detail).Append('）');
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string CategoryLabel(UiActivityCategory category) => category switch
    {
        UiActivityCategory.Navigation => "导航",
        UiActivityCategory.Selection => "选择",
        UiActivityCategory.Command => "命令",
        UiActivityCategory.DataChange => "数据变更",
        UiActivityCategory.AgentAction => "AI 操作",
        _ => "其他",
    };
}

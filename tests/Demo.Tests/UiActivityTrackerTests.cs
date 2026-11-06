using AI.Extension;

namespace Demo.Tests;

/// <summary>UI 上下文环形日志测试：这是「AI 唤醒时读取日志」策略的基础设施。</summary>
public sealed class UiActivityTrackerTests
{
    [Fact]
    public void Snapshot_ReturnsEntriesInChronologicalOrder()
    {
        var tracker = new UiActivityTracker();

        tracker.Record(UiActivityCategory.Navigation, "进入订单页");
        tracker.Record(UiActivityCategory.Selection, "选中 SO-1002");
        tracker.Record(UiActivityCategory.Command, "点击刷新");

        IReadOnlyList<UiActivityEntry> entries = tracker.Snapshot();

        Assert.Equal(3, entries.Count);
        Assert.Equal("进入订单页", entries[0].Description);
        Assert.Equal("点击刷新", entries[2].Description);
    }

    [Fact]
    public void Snapshot_TrimsToCapacity()
    {
        var tracker = new UiActivityTracker(capacity: 3);

        for (int i = 0; i < 10; i++)
        {
            tracker.Record(UiActivityCategory.Other, $"活动 {i}");
        }

        IReadOnlyList<UiActivityEntry> entries = tracker.Snapshot(100);

        Assert.Equal(3, entries.Count);
        Assert.Equal("活动 7", entries[0].Description);
        Assert.Equal("活动 9", entries[2].Description);
    }

    [Fact]
    public void Snapshot_RespectsMaxCount()
    {
        var tracker = new UiActivityTracker();

        tracker.Record(UiActivityCategory.Other, "一");
        tracker.Record(UiActivityCategory.Other, "二");
        tracker.Record(UiActivityCategory.Other, "三");

        IReadOnlyList<UiActivityEntry> entries = tracker.Snapshot(2);

        Assert.Equal(2, entries.Count);
        Assert.Equal("二", entries[0].Description);
    }

    [Fact]
    public void BuildContextSummary_FormatsReadableLines()
    {
        var tracker = new UiActivityTracker();
        tracker.Record(UiActivityCategory.DataChange, "订单 SO-1001 被取消", "客户重复下单");

        string summary = tracker.BuildContextSummary();

        Assert.Contains("数据变更", summary, StringComparison.Ordinal);
        Assert.Contains("SO-1001", summary, StringComparison.Ordinal);
        Assert.Contains("客户重复下单", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_IgnoresBlankDescriptions()
    {
        var tracker = new UiActivityTracker();

        tracker.Record(UiActivityCategory.Other, "   ");

        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void BuildContextSummary_WhenEmpty_ReturnsPlaceholder()
    {
        var tracker = new UiActivityTracker();

        Assert.Contains("暂无", tracker.BuildContextSummary(), StringComparison.Ordinal);
    }
}

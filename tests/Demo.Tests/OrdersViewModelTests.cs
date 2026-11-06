using AI.Extension;
using Demo.Model;
using Demo.ViewModel;

namespace Demo.Tests;

/// <summary>ViewModel 作为契约实现的业务行为测试（UI 与 AI 共用的业务入口）。</summary>
public sealed class OrdersViewModelTests
{
    [Fact]
    public async Task SearchAsync_FiltersByKeywordAndStatus()
    {
        OrdersViewModel viewModel = CreateViewModel();

        IReadOnlyList<Order> paid = await viewModel.SearchAsync(null, OrderStatus.Paid, CancellationToken.None);
        Assert.Single(paid);
        Assert.Equal("SO-1002", paid[0].Id);

        IReadOnlyList<Order> byCustomer = await viewModel.SearchAsync("云栖", null, CancellationToken.None);
        Assert.Equal(2, byCustomer.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNullWhenMissing()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        Assert.Null(await viewModel.GetByIdAsync("SO-9999", CancellationToken.None));

        Order? existing = await viewModel.GetByIdAsync("SO-1003", CancellationToken.None);
        Assert.NotNull(existing);
        Assert.Equal("SO-1003", viewModel.SelectedOrder?.Id);
    }

    [Fact]
    public async Task CancelAsync_RejectsCompletedOrders()
    {
        OrdersViewModel viewModel = CreateViewModel();

        OrderOperationResult result = await viewModel.CancelAsync("SO-1004", "测试", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("无法取消", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelAsync_UpdatesRepositoryAndCollection()
    {
        OrdersViewModel viewModel = CreateViewModel();

        OrderOperationResult result = await viewModel.CancelAsync("SO-1001", "客户重复下单", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(OrderStatus.Cancelled, result.Order!.Status);
        Assert.Contains("客户重复下单", result.Order.Remark!, StringComparison.Ordinal);

        Order updated = Assert.Single(viewModel.Orders, order => order.Id == "SO-1001");
        Assert.Equal(OrderStatus.Cancelled, updated.Status);
    }

    [Fact]
    public async Task GetSummaryAsync_AggregatesAllOrders()
    {
        OrdersViewModel viewModel = CreateViewModel();

        OrderSummary summary = await viewModel.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(5, summary.Total);
        Assert.Equal(229_500m, summary.TotalAmount);
        Assert.Equal(5, summary.CountByStatus.Count);
    }

    [Fact]
    public async Task UiActivityLog_TracksUserSelection_forLaterContextInjection()
    {
        var tracker = new UiActivityTracker();
        OrdersViewModel viewModel = CreateViewModel(tracker);

        await viewModel.LoadAsync();
        viewModel.SelectedOrder = viewModel.Orders.First(order => order.Id == "SO-1002");

        string summary = tracker.BuildContextSummary();

        Assert.Contains("用户进入订单管理页", summary, StringComparison.Ordinal);
        Assert.Contains("SO-1002", summary, StringComparison.Ordinal);
    }

    private static OrdersViewModel CreateViewModel(UiActivityTracker? tracker = null)
        => new(
            new InMemoryOrderRepository(),
            tracker ?? new UiActivityTracker(),
            new ImmediateUiDispatcher());
}


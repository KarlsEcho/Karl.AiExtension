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

    [Fact]
    public async Task CreateAsync_AddsOrderAndSelectsIt()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        OrderOperationResult result = await viewModel.CreateAsync("新客户", 1999.5m, "测试新增", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Order);
        Assert.Equal("新客户", result.Order!.CustomerName);
        Assert.Equal(6, viewModel.Orders.Count);
        Assert.Equal(result.Order.Id, viewModel.SelectedOrder?.Id);
        Assert.Equal("新客户", viewModel.EditCustomerName);
        Assert.Equal("1999.5", viewModel.EditAmountText);
    }

    [Fact]
    public async Task CreateAsync_ValidatesInput()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        Assert.False((await viewModel.CreateAsync("  ", 100m, null, CancellationToken.None)).Success);
        Assert.False((await viewModel.CreateAsync("客户", 0m, null, CancellationToken.None)).Success);
        Assert.Equal(5, viewModel.Orders.Count);
    }

    [Fact]
    public async Task UpdateAsync_ChangesFieldsAndRefreshesRows()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        OrderOperationResult result = await viewModel.UpdateAsync(
            "SO-1002", "改名客户", 999.9m, "改备注", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("改名客户", result.Order!.CustomerName);
        Assert.Equal(999.9m, result.Order.Amount);
        Assert.Equal("改备注", result.Order.Remark);

        Order row = Assert.Single(viewModel.Orders, order => order.Id == "SO-1002");
        Assert.Equal("改名客户", row.CustomerName);
    }

    [Fact]
    public async Task UpdateAsync_NullFieldsKeepOriginalValues()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        Order before = viewModel.Orders.Single(order => order.Id == "SO-1003");
        OrderOperationResult result = await viewModel.UpdateAsync("SO-1003", null, null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(before.CustomerName, result.Order!.CustomerName);
        Assert.Equal(before.Amount, result.Order.Amount);
        Assert.Equal(before.Remark, result.Order.Remark);
    }

    [Fact]
    public async Task ChangeStatusAsync_RejectsTerminalAndUpdatesNormalOrders()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        OrderOperationResult rejected = await viewModel.ChangeStatusAsync("SO-1004", OrderStatus.Shipped, CancellationToken.None);
        Assert.False(rejected.Success);

        OrderOperationResult changed = await viewModel.ChangeStatusAsync("SO-1001", OrderStatus.Paid, CancellationToken.None);
        Assert.True(changed.Success);
        Assert.Equal(OrderStatus.Paid, changed.Order!.Status);
    }

    [Fact]
    public async Task DeleteAsync_RemovesOrderAndReselects()
    {
        OrdersViewModel viewModel = CreateViewModel();
        await viewModel.LoadAsync();

        OrderOperationResult result = await viewModel.DeleteAsync("SO-1003", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(4, viewModel.Orders.Count);
        Assert.DoesNotContain(viewModel.Orders, order => order.Id == "SO-1003");
        Assert.NotNull(viewModel.SelectedOrder);
    }

    private static OrdersViewModel CreateViewModel(UiActivityTracker? tracker = null)
        => new(
            new InMemoryOrderRepository(),
            tracker ?? new UiActivityTracker(),
            new ImmediateUiDispatcher());
}


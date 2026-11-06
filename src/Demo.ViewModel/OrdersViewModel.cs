using System.Collections.ObjectModel;
using AI.Extension;
using AI.Extension.Mvvm;
using Demo.Contract;
using Demo.Model;

namespace Demo.ViewModel;

/// <summary>
/// 订单视图模型。
/// <para>
/// 关键点：它同时是 WPF 界面的数据源，也是 <see cref="IOrderService"/> 契约的实现。
/// 也就是说「人类点鼠标」和「AI 调工具」最终走的是同一段业务逻辑，
/// 区别只在于入口：UI 命令 vs MCP 工具封装。
/// </para>
/// </summary>
public sealed class OrdersViewModel : ObservableObject, IOrderService
{
    private readonly IOrderRepository _repository;
    private readonly IUiActivityTracker _activityTracker;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ObservableCollection<Order> _orders = new();

    private Order? _selectedOrder;
    private string _statusMessage = "就绪";
    private bool _isBusy;

    /// <summary>构造视图模型。</summary>
    public OrdersViewModel(
        IOrderRepository repository,
        IUiActivityTracker activityTracker,
        IUiDispatcher uiDispatcher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        RefreshCommand = new AsyncRelayCommand(_ => LoadAsync());
        CancelSelectedCommand = new AsyncRelayCommand(_ => CancelSelectedAsync(), _ => SelectedOrder is not null);
    }

    /// <summary>绑定的订单集合。</summary>
    public ObservableCollection<Order> Orders => _orders;

    /// <summary>当前选中的订单。</summary>
    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value) && value is not null)
            {
                _activityTracker.Record(UiActivityCategory.Selection, $"用户选中订单 {value.Id}（{value.CustomerName}）");
                CancelSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>状态栏文案。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>是否忙碌。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>刷新（UI 按钮）。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>取消当前选中订单（UI 按钮）。</summary>
    public AsyncRelayCommand CancelSelectedCommand { get; }

    /// <summary>界面启动后加载数据。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _uiDispatcher.InvokeAsync(async () =>
        {
            IsBusy = true;
            StatusMessage = "正在加载订单…";
            await Task.Yield();
            return true;
        }).ConfigureAwait(false);

        try
        {
            IReadOnlyList<Order> orders = _repository.GetAll();

            await _uiDispatcher.InvokeAsync(() =>
            {
                _orders.Clear();
                foreach (Order order in orders)
                {
                    _orders.Add(order);
                }

                SelectedOrder = _orders.FirstOrDefault();
                StatusMessage = $"已加载 {orders.Count} 条订单";
                _activityTracker.Record(UiActivityCategory.Navigation, "用户进入订单管理页", $"{orders.Count} 条订单");
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                IsBusy = false;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    // ===== IOrderService 契约实现（UI 与 AI 共用的业务入口） =====

    /// <inheritdoc />
    public async Task<IReadOnlyList<Order>> SearchAsync(
        string? keyword,
        OrderStatus? status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order[] matches = _repository.GetAll()
            .Where(order => status is null || order.Status == status)
            .Where(order => string.IsNullOrWhiteSpace(keyword) || Matches(order, keyword))
            .ToArray();

        await ReplaceOrdersAsync(matches, $"检索到 {matches.Length} 条订单").ConfigureAwait(false);
        return matches;
    }

    /// <inheritdoc />
    public async Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order? order = _repository.Find(orderId);
        if (order is null)
        {
            return null;
        }

        // 让界面跟随 AI 的查询结果：数据变化通过 UI 线程调度回主线程
        await _uiDispatcher.InvokeAsync(() =>
        {
            Order? target = _orders.FirstOrDefault(candidate => string.Equals(candidate.Id, order.Id, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                SelectedOrder = target;
            }

            StatusMessage = $"定位订单 {order.Id}";
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return order;
    }

    /// <inheritdoc />
    public async Task<OrderOperationResult> CancelAsync(
        string orderId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order? existing = _repository.Find(orderId);
        if (existing is null)
        {
            return new OrderOperationResult(false, $"订单 {orderId} 不存在。", null);
        }

        if (existing.Status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            return new OrderOperationResult(
                false,
                $"订单 {existing.Id} 当前状态为 {existing.Status}，无法取消。",
                existing);
        }

        Order cancelled = existing with
        {
            Status = OrderStatus.Cancelled,
            Remark = string.IsNullOrWhiteSpace(existing.Remark) ? reason : $"{existing.Remark}；{reason}",
        };

        if (!_repository.TryUpdate(cancelled))
        {
            return new OrderOperationResult(false, $"订单 {orderId} 更新失败。", existing);
        }

        await ReplaceOrdersAsync(_repository.GetAll(), $"订单 {cancelled.Id} 已取消").ConfigureAwait(false);

        await _uiDispatcher.InvokeAsync(() =>
        {
            SelectedOrder = _orders.FirstOrDefault(candidate => string.Equals(candidate.Id, cancelled.Id, StringComparison.OrdinalIgnoreCase));
            _activityTracker.Record(UiActivityCategory.DataChange, $"订单 {cancelled.Id} 被取消", reason);
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        return new OrderOperationResult(true, $"订单 {cancelled.Id} 已取消：{reason}", cancelled);
    }

    /// <inheritdoc />
    public Task<OrderSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Order> all = _repository.GetAll();
        OrderSummary summary = new(
            Total: all.Count,
            TotalAmount: all.Sum(order => order.Amount),
            CountByStatus: all
                .GroupBy(order => order.Status)
                .ToDictionary(group => group.Key, group => group.Count()));

        return Task.FromResult(summary);
    }

    /// <inheritdoc />
    public void RefreshUi() => OnPropertyChanged(nameof(Orders));

    private async Task CancelSelectedAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        OrderOperationResult result = await CancelAsync(SelectedOrder.Id, "用户在界面上取消").ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    private async Task ReplaceOrdersAsync(IReadOnlyList<Order> orders, string statusMessage)
    {
        await _uiDispatcher.InvokeAsync(() =>
        {
            _orders.Clear();
            foreach (Order order in orders)
            {
                _orders.Add(order);
            }

            StatusMessage = statusMessage;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private static bool Matches(Order order, string keyword) =>
        order.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        order.CustomerName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (order.Remark?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);
}

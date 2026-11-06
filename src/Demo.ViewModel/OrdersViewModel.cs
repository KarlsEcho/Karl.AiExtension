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
    private const decimal MaxAmount = 1_000_000_000m;

    private readonly IOrderRepository _repository;
    private readonly IUiActivityTracker _activityTracker;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ObservableCollection<Order> _orders = new();

    private Order? _selectedOrder;
    private string _statusMessage = "就绪";
    private bool _isBusy;
    private string _editCustomerName = string.Empty;
    private string _editAmountText = string.Empty;
    private string _editRemark = string.Empty;

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
        CreateOrderCommand = new AsyncRelayCommand(_ => CreateFromEditorAsync());
        UpdateSelectedOrderCommand = new AsyncRelayCommand(
            _ => UpdateFromEditorAsync(),
            _ => SelectedOrder is not null);
        DeleteSelectedOrderCommand = new AsyncRelayCommand(
            _ => DeleteSelectedAsync(),
            _ => SelectedOrder is not null);
        ClearEditorCommand = new RelayCommand(_ => SyncEditorFromSelection());
    }

    /// <summary>绑定的订单集合。</summary>
    public ObservableCollection<Order> Orders => _orders;

    /// <summary>当前选中的订单。</summary>
    public Order? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                SyncEditorFromSelection();
                OnPropertyChanged(nameof(HasSelection));
                CancelSelectedCommand.RaiseCanExecuteChanged();
                UpdateSelectedOrderCommand.RaiseCanExecuteChanged();
                DeleteSelectedOrderCommand.RaiseCanExecuteChanged();

                if (value is not null)
                {
                    _activityTracker.Record(UiActivityCategory.Selection, $"用户选中订单 {value.Id}（{value.CustomerName}）");
                }
            }
        }
    }

    /// <summary>是否有选中订单。</summary>
    public bool HasSelection => _selectedOrder is not null;

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

    /// <summary>编辑区：客户名称（同时用于新增与修改）。</summary>
    public string EditCustomerName
    {
        get => _editCustomerName;
        set => SetProperty(ref _editCustomerName, value);
    }

    /// <summary>编辑区：金额文本。</summary>
    public string EditAmountText
    {
        get => _editAmountText;
        set => SetProperty(ref _editAmountText, value);
    }

    /// <summary>编辑区：备注。</summary>
    public string EditRemark
    {
        get => _editRemark;
        set => SetProperty(ref _editRemark, value);
    }

    /// <summary>刷新（UI 按钮）。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>取消当前选中订单（UI 按钮）。</summary>
    public AsyncRelayCommand CancelSelectedCommand { get; }

    /// <summary>新增订单（用编辑区内容）。</summary>
    public AsyncRelayCommand CreateOrderCommand { get; }

    /// <summary>把编辑区内容保存到当前选中订单。</summary>
    public AsyncRelayCommand UpdateSelectedOrderCommand { get; }

    /// <summary>删除当前选中订单。</summary>
    public AsyncRelayCommand DeleteSelectedOrderCommand { get; }

    /// <summary>把编辑区恢复成当前选中订单的内容。</summary>
    public RelayCommand ClearEditorCommand { get; }

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
            return Fail($"订单 {orderId} 不存在。", null);
        }

        if (existing.Status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            return Fail($"订单 {existing.Id} 当前状态为 {existing.Status}，无法取消。", existing);
        }

        Order cancelled = existing with
        {
            Status = OrderStatus.Cancelled,
            Remark = string.IsNullOrWhiteSpace(existing.Remark) ? reason : $"{existing.Remark}；{reason}",
        };

        if (!_repository.TryUpdate(cancelled))
        {
            return Fail($"订单 {orderId} 更新失败。", existing);
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
    public async Task<OrderOperationResult> CreateAsync(
        string customerName,
        decimal amount,
        string? remark,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string name = customerName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Fail("客户名称不能为空。", null);
        }

        if (name.Length > 100)
        {
            return Fail("客户名称过长（最多 100 个字符）。", null);
        }

        if (amount <= 0 || amount > MaxAmount)
        {
            return Fail("金额必须大于 0 且不超过 10 亿。", null);
        }

        string id = NextOrderId();
        var order = new Order
        {
            Id = id,
            CustomerName = name,
            Amount = decimal.Round(amount, 2),
            Status = OrderStatus.Pending,
            CreatedAt = DateTimeOffset.Now,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };

        try
        {
            _repository.Add(order);
        }
        catch (InvalidOperationException exception)
        {
            return Fail(exception.Message, null);
        }

        await RefreshAndSelectAsync(order.Id, $"订单 {id} 已创建：{name} ¥{order.Amount:N2}").ConfigureAwait(false);
        _activityTracker.Record(UiActivityCategory.DataChange, $"订单 {id} 已创建", $"{name} ¥{order.Amount:N2}");

        return new OrderOperationResult(true, $"订单 {id} 已创建：{name} ¥{order.Amount:N2}", order);
    }

    /// <inheritdoc />
    public async Task<OrderOperationResult> UpdateAsync(
        string orderId,
        string? customerName,
        decimal? amount,
        string? remark,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order? existing = _repository.Find(orderId);
        if (existing is null)
        {
            return Fail($"订单 {orderId} 不存在。", null);
        }

        string? name = string.IsNullOrWhiteSpace(customerName) ? null : customerName.Trim();
        if (name is { Length: > 100 })
        {
            return Fail("客户名称过长（最多 100 个字符）。", existing);
        }

        if (amount is <= 0 or > MaxAmount)
        {
            return Fail("金额必须大于 0 且不超过 10 亿。", existing);
        }

        Order updated = existing with
        {
            CustomerName = name ?? existing.CustomerName,
            Amount = amount is null ? existing.Amount : decimal.Round(amount.Value, 2),
            Remark = remark is null
                ? existing.Remark
                : (string.IsNullOrWhiteSpace(remark) ? null : remark.Trim()),
        };

        if (!_repository.TryUpdate(updated))
        {
            return Fail($"订单 {orderId} 更新失败。", existing);
        }

        await RefreshAndSelectAsync(updated.Id, $"订单 {updated.Id} 已更新").ConfigureAwait(false);
        _activityTracker.Record(UiActivityCategory.DataChange, $"订单 {updated.Id} 已修改");

        return new OrderOperationResult(true, $"订单 {updated.Id} 已更新：{updated.CustomerName} ¥{updated.Amount:N2}", updated);
    }

    /// <inheritdoc />
    public async Task<OrderOperationResult> ChangeStatusAsync(
        string orderId,
        OrderStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order? existing = _repository.Find(orderId);
        if (existing is null)
        {
            return Fail($"订单 {orderId} 不存在。", null);
        }

        if (existing.Status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            return Fail($"订单 {existing.Id} 已是终态（{existing.Status}），不能再变更状态。", existing);
        }

        if (status == OrderStatus.Cancelled)
        {
            return Fail("取消订单请使用取消操作（order_cancel），以便记录原因。", existing);
        }

        if (existing.Status == status)
        {
            return new OrderOperationResult(true, $"订单 {existing.Id} 已经是 {status} 状态。", existing);
        }

        Order updated = existing with { Status = status };
        if (!_repository.TryUpdate(updated))
        {
            return Fail($"订单 {orderId} 更新失败。", existing);
        }

        await RefreshAndSelectAsync(updated.Id, $"订单 {updated.Id} 状态已改为 {status}").ConfigureAwait(false);
        _activityTracker.Record(UiActivityCategory.DataChange, $"订单 {updated.Id} 状态变更为 {status}");

        return new OrderOperationResult(true, $"订单 {updated.Id} 状态已改为 {status}。", updated);
    }

    /// <inheritdoc />
    public async Task<OrderOperationResult> DeleteAsync(string orderId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Order? existing = _repository.Find(orderId);
        if (existing is null)
        {
            return Fail($"订单 {orderId} 不存在。", null);
        }

        if (!_repository.Remove(orderId))
        {
            return Fail($"订单 {orderId} 删除失败。", existing);
        }

        await ReplaceOrdersAsync(_repository.GetAll(), $"订单 {existing.Id} 已删除").ConfigureAwait(false);
        await _uiDispatcher.InvokeAsync(() =>
        {
            SelectedOrder = _orders.FirstOrDefault();
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        _activityTracker.Record(UiActivityCategory.DataChange, $"订单 {existing.Id} 已删除", existing.CustomerName);
        return new OrderOperationResult(true, $"订单 {existing.Id} 已删除。", existing);
    }

    // ===== UI 命令实现 =====

    private async Task CancelSelectedAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        OrderOperationResult result = await CancelAsync(SelectedOrder.Id, "用户在界面上取消").ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    private async Task CreateFromEditorAsync()
    {
        string name = EditCustomerName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "新增失败：请填写客户名称。";
            return;
        }

        if (!decimal.TryParse(EditAmountText, out decimal amount) || amount <= 0)
        {
            StatusMessage = "新增失败：金额必须是大于 0 的数字。";
            return;
        }

        OrderOperationResult result = await CreateAsync(name, amount, EditRemark).ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    private async Task UpdateFromEditorAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        if (!decimal.TryParse(EditAmountText, out decimal amount) || amount <= 0)
        {
            StatusMessage = "保存失败：金额必须是大于 0 的数字。";
            return;
        }

        OrderOperationResult result = await UpdateAsync(
            SelectedOrder.Id,
            EditCustomerName,
            amount,
            EditRemark).ConfigureAwait(true);

        StatusMessage = result.Message;
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedOrder is null)
        {
            return;
        }

        OrderOperationResult result = await DeleteAsync(SelectedOrder.Id).ConfigureAwait(true);
        StatusMessage = result.Message;
    }

    // ===== 内部辅助 =====

    private string NextOrderId()
    {
        int max = 1000;
        foreach (Order order in _repository.GetAll())
        {
            if (order.Id.StartsWith("SO-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(order.Id.AsSpan(3), out int number))
            {
                max = Math.Max(max, number);
            }
        }

        string candidate = "SO-" + (max + 1);
        while (_repository.Find(candidate) is not null)
        {
            max++;
            candidate = "SO-" + (max + 1);
        }

        return candidate;
    }

    private async Task RefreshAndSelectAsync(string orderId, string statusMessage)
    {
        await ReplaceOrdersAsync(_repository.GetAll(), statusMessage).ConfigureAwait(false);
        await _uiDispatcher.InvokeAsync(() =>
        {
            SelectedOrder = _orders.FirstOrDefault(candidate => string.Equals(candidate.Id, orderId, StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        }).ConfigureAwait(false);
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

            // 集合内容变化不会自动触发 PropertyChanged；通知一次便于 Blazor 等非 WPF 宿主刷新
            OnPropertyChanged(nameof(Orders));
            StatusMessage = statusMessage;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private void SyncEditorFromSelection()
    {
        if (_selectedOrder is null)
        {
            EditCustomerName = string.Empty;
            EditAmountText = string.Empty;
            EditRemark = string.Empty;
            return;
        }

        EditCustomerName = _selectedOrder.CustomerName;
        EditAmountText = _selectedOrder.Amount.ToString("0.##");
        EditRemark = _selectedOrder.Remark ?? string.Empty;
    }

    private static bool Matches(Order order, string keyword) =>
        order.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        order.CustomerName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        (order.Remark?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false);

    private static OrderOperationResult Fail(string message, Order? order = null) => new(false, message, order);
}

namespace Demo.Model;

/// <summary>订单状态。</summary>
public enum OrderStatus
{
    /// <summary>待支付。</summary>
    Pending,

    /// <summary>已支付。</summary>
    Paid,

    /// <summary>已发货。</summary>
    Shipped,

    /// <summary>已完成。</summary>
    Completed,

    /// <summary>已取消。</summary>
    Cancelled,
}

/// <summary>订单（演示用领域模型）。</summary>
public sealed record Order
{
    /// <summary>订单号。</summary>
    public required string Id { get; init; }

    /// <summary>客户名称。</summary>
    public required string CustomerName { get; init; }

    /// <summary>金额。</summary>
    public decimal Amount { get; init; }

    /// <summary>状态。</summary>
    public OrderStatus Status { get; init; }

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>备注。</summary>
    public string? Remark { get; init; }
}

/// <summary>订单操作的统一结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="Message">给人类/AI 看的说明。</param>
/// <param name="Order">操作后的订单快照（可为空）。</param>
public sealed record OrderOperationResult(bool Success, string Message, Order? Order);

/// <summary>订单统计摘要。</summary>
/// <param name="Total">订单总数。</param>
/// <param name="TotalAmount">总金额。</param>
/// <param name="CountByStatus">各状态数量。</param>
public sealed record OrderSummary(int Total, decimal TotalAmount, IReadOnlyDictionary<OrderStatus, int> CountByStatus);

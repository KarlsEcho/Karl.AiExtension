using Demo.Model;

namespace Demo.Contract;

/// <summary>
/// 订单业务契约。</summary>
/// <remarks>
/// <b>本层对 AI 完全无感知</b>：不引用任何 AI/MCP 包、不标注任何 AI 特性。
/// 「它要暴露成哪些 MCP 工具、工具名与描述是什么」由 MCP 层
/// （<c>Demo.MCP</c> 的 <c>[AiContractBinding]</c> 声明与手写封装）主动承担。
/// </remarks>
public interface IOrderService
{
    /// <summary>按关键词（订单号 / 客户名 / 备注）与状态检索订单。</summary>
    Task<IReadOnlyList<Order>> SearchAsync(
        string? keyword,
        OrderStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>按订单号获取订单详情；不存在时返回 null。</summary>
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>取消订单并记录原因。</summary>
    Task<OrderOperationResult> CancelAsync(
        string orderId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>返回订单统计摘要。</summary>
    Task<OrderSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>新增订单（默认状态为待支付）。</summary>
    Task<OrderOperationResult> CreateAsync(
        string customerName,
        decimal amount,
        string? remark,
        CancellationToken cancellationToken = default);

    /// <summary>修改订单信息；参数为空表示不修改该字段。</summary>
    Task<OrderOperationResult> UpdateAsync(
        string orderId,
        string? customerName,
        decimal? amount,
        string? remark,
        CancellationToken cancellationToken = default);

    /// <summary>变更订单状态（支付 / 发货 / 完成等）。</summary>
    Task<OrderOperationResult> ChangeStatusAsync(
        string orderId,
        OrderStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>删除订单（物理删除，谨慎使用）。</summary>
    Task<OrderOperationResult> DeleteAsync(string orderId, CancellationToken cancellationToken = default);
}

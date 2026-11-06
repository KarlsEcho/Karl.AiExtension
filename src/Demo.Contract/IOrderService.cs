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
}

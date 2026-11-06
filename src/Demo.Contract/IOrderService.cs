using AI.Extension;
using Demo.Model;

namespace Demo.Contract;

/// <summary>
/// 面向 AI 的订单契约：只声明「AI 能做什么」，不含任何实现。
/// <para>
/// 源生成器会据此在 Demo.MCP 中生成 <c>OrderServiceBridge</c>（强类型转发基础设施）
/// 与 <c>AddAiContractBridges()</c> 注册扩展。
/// </para>
/// </summary>
[AiContract(Name = "OrderService")]
public interface IOrderService
{
    /// <summary>按关键词（订单号 / 客户名 / 备注）与状态检索订单。</summary>
    [AiTool("按关键词与状态检索订单，返回匹配的订单列表。关键词可为空表示不按关键词过滤。", Name = "order_search")]
    Task<IReadOnlyList<Order>> SearchAsync(
        string? keyword,
        OrderStatus? status,
        CancellationToken cancellationToken = default);

    /// <summary>按订单号获取订单详情。</summary>
    [AiTool("按订单号精确获取订单详情；订单不存在时返回 null。", Name = "order_get")]
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>取消订单并记录原因。</summary>
    [AiTool("取消指定订单并记录取消原因。已完成或已取消的订单无法取消，会返回失败结果。", Name = "order_cancel")]
    Task<OrderOperationResult> CancelAsync(
        string orderId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>返回订单统计摘要。</summary>
    [AiTool("统计当前订单：总数、总金额与各状态数量。", Name = "order_summary")]
    Task<OrderSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>纯 UI 刷新动作：语义不适合暴露给 AI，因此显式忽略。</summary>
    [AiIgnore]
    void RefreshUi();
}

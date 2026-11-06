using AI.Extension;
using Demo.Contract;

namespace Demo.MCP.Ai;

/// <summary>
/// 把 <see cref="IOrderService"/> 桥接进 MCP 层。
/// <para>
/// 这个文件体现依赖方向：<b>MCP 层主动依赖契约层</b>。
/// 契约层（Demo.Contract）不引用 AI.Extension.Abstractions、不打任何 AI 特性；
/// 「这个契约要变成哪些 AI 工具、工具名与描述是什么」全部由本层声明与手写封装承担。
/// </para>
/// <para>
/// 源生成器读取本声明，在 Demo.MCP 中生成 <c>OrderServiceBridge</c>（强类型转发基础设施）
/// 与 <c>AddAiContractBridges()</c> 注册扩展。
/// </para>
/// </summary>
[AiContractBinding(typeof(IOrderService), Name = "OrderService")]
[AiMethod(nameof(IOrderService.SearchAsync), ToolName = "order_search",
    Description = "按关键词与状态检索订单，返回匹配的订单列表。关键词可为空表示不按关键词过滤。")]
[AiMethod(nameof(IOrderService.GetByIdAsync), ToolName = "order_get",
    Description = "按订单号精确获取订单详情；订单不存在时返回 null。")]
[AiMethod(nameof(IOrderService.CancelAsync), ToolName = "order_cancel",
    Description = "取消指定订单并记录取消原因。已完成或已取消的订单无法取消，会返回失败结果。")]
[AiMethod(nameof(IOrderService.GetSummaryAsync), ToolName = "order_summary",
    Description = "统计当前订单：总数、总金额与各状态数量。")]
internal sealed class OrderServiceBinding;

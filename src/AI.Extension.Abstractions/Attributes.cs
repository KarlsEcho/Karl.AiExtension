namespace AI.Extension;

/// <summary>
/// 在 <b>MCP 层</b>声明「把某个业务契约桥接进来」。
/// <para>
/// 依赖方向是刻意的：<c>MCP 层 ──► 契约层</c>，契约层对 AI 一无所知（不引用本包、不打任何 AI 特性）。
/// 桥接基础设施由源生成器按这个声明产出，落在声明所在的程序集（通常是 Demo.MCP）。
/// </para>
/// </summary>
/// <example>
/// <code>
/// [AiContractBinding(typeof(Demo.Contract.IOrderService))]
/// [AiMethod(nameof(Demo.Contract.IOrderService.SearchAsync), ToolName = "order_search", Description = "检索订单")]
/// internal sealed class OrderServiceBinding;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AiContractBindingAttribute : Attribute
{
    /// <summary>声明要桥接的契约接口。</summary>
    /// <param name="contractType">契约接口类型（必须是 interface）。</param>
    public AiContractBindingAttribute(Type contractType) => ContractType = contractType;

    /// <summary>被桥接的契约接口类型。</summary>
    public Type ContractType { get; }

    /// <summary>
    /// 桥接名。用于生成桥接类名（<c>{Name}Bridge</c>）与契约清单。
    /// 为空时取接口名去掉前导 <c>I</c>，例如 <c>IOrderService</c> → <c>OrderService</c>。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>生成的桥接类在 DI 容器中的生命周期，默认 <see cref="AiBridgeLifetime.Transient"/>。</summary>
    public AiBridgeLifetime Lifetime { get; set; } = AiBridgeLifetime.Transient;

    /// <summary>是否在生成的 <c>AddAiContractBridges()</c> 中注册该桥接，默认 true。</summary>
    public bool GenerateRegistration { get; set; } = true;
}

/// <summary>
/// 可选：为某个契约方法补充「契约 → 工具」的追踪元数据。
/// <para>
/// 只影响契约清单（诊断面板、测试断言、文档生成），<b>不影响模型</b>——
/// 真正给大模型看的工具名与描述仍然由 MCP 层手写封装上的
/// <c>[McpServerTool(Name = ...)]</c> / <c>[Description]</c> 决定。
/// 这里登记 ToolName 的价值是：测试可以断言「契约映射的工具名」与「MCP 实际暴露的工具名」没有漂移。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AiMethodAttribute : Attribute
{
    /// <summary>声明契约方法上的语义。</summary>
    /// <param name="methodName">契约方法名，建议写 <c>nameof(契约接口.方法)</c> 以获得编译期校验。</param>
    public AiMethodAttribute(string methodName) => MethodName = methodName;

    /// <summary>契约方法名。</summary>
    public string MethodName { get; }

    /// <summary>建议的 MCP 工具名（用于对齐校验，不直接生效）。</summary>
    public string? ToolName { get; set; }

    /// <summary>给 AI 看的语义描述（用于契约清单，不直接生效）。</summary>
    public string? Description { get; set; }
}

/// <summary>桥接类的 DI 生命周期。</summary>
public enum AiBridgeLifetime
{
    /// <summary>每次解析创建新实例（默认，最安全，不会捕获依赖）。</summary>
    Transient,

    /// <summary>每个作用域一个实例。</summary>
    Scoped,

    /// <summary>单例。注意：绑定到单例契约实现时才可使用。</summary>
    Singleton,
}

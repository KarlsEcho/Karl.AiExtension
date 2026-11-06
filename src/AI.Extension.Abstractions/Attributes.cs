namespace AI.Extension;

/// <summary>
/// 将一个接口标记为「AI 契约」。
/// <para>
/// 源生成器会扫描所有标记了 <see cref="AiContractAttribute"/> 的接口，
/// 生成：① 强类型转发桥接类（基础设施）；② DI 注册扩展；③ 契约目录清单。
/// 开发者随后在 MCP 层手写轻量语义封装（打上微软 MCP SDK 的 [McpServerTool]），
/// 在封装方法体内调用生成的桥接方法即可。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class AiContractAttribute : Attribute
{
    /// <summary>使用接口名（去掉前导 I）作为契约名。</summary>
    public AiContractAttribute()
    {
    }

    /// <summary>显式指定契约名。</summary>
    public AiContractAttribute(string name) => Name = name;

    /// <summary>
    /// 契约名。用于生成的桥接类名（<c>{Name}Bridge</c>）、工具名建议前缀与契约目录。
    /// 为空时取接口名去掉前导 <c>I</c>，例如 <c>IOrderService</c> → <c>OrderService</c>。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>生成的桥接类在 DI 容器中的生命周期，默认 <see cref="AiBridgeLifetime.Transient"/>。</summary>
    public AiBridgeLifetime Lifetime { get; set; } = AiBridgeLifetime.Transient;

    /// <summary>是否在生成的 <c>AddAiContractBridges()</c> 中注册该桥接，默认 true。</summary>
    public bool GenerateRegistration { get; set; } = true;
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

/// <summary>
/// 为一个契约方法补充 AI 语义。可选：即使不打这个特性，方法也会被生成桥接，
/// 只是工具名建议与描述为空。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AiToolAttribute : Attribute
{
    /// <summary>使用方法名自动推断描述。</summary>
    public AiToolAttribute()
    {
    }

    /// <summary>提供给 AI 看的语义描述。</summary>
    public AiToolAttribute(string description) => Description = description;

    /// <summary>给 AI 看的语义描述（会进入工具名建议与契约清单）。</summary>
    public string? Description { get; set; }

    /// <summary>
    /// 建议的 MCP 工具名。最终工具名由开发者手写的封装方法上的 <c>[McpServerTool(Name = ...)]</c> 决定，
    /// 这里只是让「契约 → 工具」的对应关系可被追踪（IDE 提示、契约清单、测试断言）。
    /// </summary>
    public string? Name { get; set; }
}

/// <summary>生成桥接时忽略该方法或参数。</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class AiIgnoreAttribute : Attribute
{
}

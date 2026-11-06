namespace AI.Extension;

/// <summary>
/// 源生成器产出的桥接类统一实现的标记接口。
/// <para>
/// 桥接类是「契约 → 基础设施」的产物：它持有契约实现（通常来自 Demo.ViewModel 的 DI 注册），
/// 并把契约方法逐一转发出去。手写的 MCP 轻量封装调用桥接，而不是直接依赖 ViewModel，
/// 从而保证 MCP 层与 ViewModel 层零耦合。
/// </para>
/// </summary>
public interface IAiContractBridge
{
    /// <summary>被桥接的契约接口类型。</summary>
    Type ContractType { get; }

    /// <summary>契约名（默认是接口名去掉前导 I）。</summary>
    string ContractName { get; }

    /// <summary>桥接出的方法元数据，供契约清单、IDE 与测试使用。</summary>
    IReadOnlyList<AiBridgeMethod> Methods { get; }
}

/// <summary>一个被桥接的契约方法。</summary>
/// <param name="MethodName">契约方法名。</param>
/// <param name="Description">给 AI 看的语义描述（可空）。</param>
/// <param name="SuggestedToolName">建议的 MCP 工具名。</param>
public sealed record AiBridgeMethod(string MethodName, string? Description, string SuggestedToolName);

/// <summary>契约的静态描述，用于运行时自省（诊断面板、测试、文档生成）。</summary>
public sealed record AiContractDescriptor(
    string ContractFullName,
    string ContractName,
    IReadOnlyList<AiBridgeMethod> Methods);

/// <summary>
/// 运行时可枚举的契约桥接目录。由生成器生成的注册代码自动注册为单例，
/// 因此 UI 的诊断面板可以直接列出「当前应用暴露了哪些 AI 契约」。
/// </summary>
public sealed class AiContractCatalog
{
    /// <summary>构造契约目录。</summary>
    public AiContractCatalog(IEnumerable<IAiContractBridge> bridges)
    {
        Bridges = (bridges ?? throw new ArgumentNullException(nameof(bridges))).ToArray();
        Contracts = Bridges
            .Select(static bridge => new AiContractDescriptor(
                bridge.ContractType.FullName ?? bridge.ContractType.Name,
                bridge.ContractName,
                bridge.Methods))
            .ToArray();
    }

    /// <summary>所有桥接实例。</summary>
    public IReadOnlyList<IAiContractBridge> Bridges { get; }

    /// <summary>所有契约描述。</summary>
    public IReadOnlyList<AiContractDescriptor> Contracts { get; }
}

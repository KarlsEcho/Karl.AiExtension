namespace AI.Extension.Abstractions.Generator.Internal;

/// <summary>一个可桥接的契约方法。</summary>
internal sealed record MethodSpec(
    string Name,
    string ReturnTypeName,
    EquatableArray<ParamSpec> Parameters,
    string Description,
    string SuggestedToolName);

/// <summary>契约方法的一个参数。</summary>
internal sealed record ParamSpec(
    string TypeName,
    string Name,
    string? DefaultValueExpression);

/// <summary>一个被 MCP 层绑定的契约接口的生成模型。</summary>
internal sealed record ContractSpec(
    string InterfaceFullName,
    string ContractFullName,
    string BridgeNamespace,
    string BridgeName,
    string ContractName,
    string Lifetime,
    bool GenerateRegistration,
    EquatableArray<MethodSpec> Methods);

/// <summary>单个绑定声明（<c>[AiContractBinding]</c>）的收集结果。</summary>
internal sealed record BindingResult(
    ContractSpec? Spec,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>本次编译中收集到的全部契约与诊断。</summary>
internal sealed record ContractCollection(
    EquatableArray<ContractSpec> Contracts,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static readonly ContractCollection Empty =
        new(EquatableArray<ContractSpec>.Empty, EquatableArray<DiagnosticInfo>.Empty);
}

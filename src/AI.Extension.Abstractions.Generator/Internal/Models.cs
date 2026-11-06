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
    string? DefaultValueExpression,
    bool Omit);

/// <summary>一个 AI 契约接口的生成模型。</summary>
internal sealed record ContractSpec(
    string InterfaceFullName,
    string ContractFullName,
    string BridgeNamespace,
    string BridgeName,
    string ContractName,
    string Lifetime,
    bool GenerateRegistration,
    EquatableArray<MethodSpec> Methods);

/// <summary>本次编译中收集到的全部契约与诊断。</summary>
internal sealed record ContractCollection(
    EquatableArray<ContractSpec> Contracts,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public static readonly ContractCollection Empty =
        new(EquatableArray<ContractSpec>.Empty, EquatableArray<DiagnosticInfo>.Empty);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace AI.Extension.Abstractions.Generator.Internal;

/// <summary>
/// 按 MCP 层的 <c>[AiContractBinding]</c> 声明收集契约。
/// <para>
/// 依赖方向：MCP 层通过 <c>typeof(契约接口)</c> 主动引用契约层；契约层本身完全不知道 AI 的存在。
/// 因为绑定声明就写在 MCP 程序集里，这里可以直接用语法级增量 Provider（不需要再扫描引用程序集）。
/// </para>
/// </summary>
internal static class BindingCollector
{
    private const string BindingAttributeMetadataName = "AI.Extension.AiContractBindingAttribute";
    private const string MethodAttributeMetadataName = "AI.Extension.AiMethodAttribute";
    private const string GeneratedRootNamespace = "AI.Extension.Generated";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static BindingResult Build(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (context.TargetSymbol is not INamedTypeSymbol anchor || context.Attributes.Length == 0)
        {
            return new BindingResult(null, EquatableArray<DiagnosticInfo>.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        AttributeData binding = context.Attributes[0];

        INamedTypeSymbol? contract = binding.ConstructorArguments.Length > 0
            ? binding.ConstructorArguments[0].Value as INamedTypeSymbol
            : null;

        if (contract is null || contract.TypeKind != TypeKind.Interface)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.BindingTargetNotInterface,
                $"{anchor.ToDisplayString()} 上的 [AiContractBinding] 目标不是可解析的接口类型。",
                anchor.Locations.FirstOrDefault()));
            return new BindingResult(null, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        string contractName = ReadString(binding, "Name") ?? TrimContractPrefix(contract.Name);
        if (contractName.Length == 0)
        {
            contractName = contract.Name;
        }

        string lifetime = ReadLifetime(binding);
        bool generateRegistration = ReadBoolean(binding, "GenerateRegistration") ?? true;

        Dictionary<string, (string? ToolName, string? Description)> methodMetadata =
            ReadMethodMetadata(context, anchor, diagnostics);

        var methods = new List<MethodSpec>();

        foreach (ISymbol member in contract.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.NonMethodMemberIgnored,
                    $"契约 {contract.Name} 的成员 {member.Name} 不是方法，已忽略。",
                    member.Locations.FirstOrDefault()));
                continue;
            }

            if (method.MethodKind != MethodKind.Ordinary)
            {
                continue; // 属性/事件访问器
            }

            if (method.IsStatic)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.UnsupportedContractMethod,
                    $"契约方法 {contract.Name}.{method.Name} 是静态成员，源生成器只桥接实例方法，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            if (method.TypeParameters.Length > 0)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.UnsupportedContractMethod,
                    $"契约方法 {contract.Name}.{method.Name} 是泛型方法，AI 工具无法表达泛型，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            if (method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.UnsupportedContractMethod,
                    $"契约方法 {contract.Name}.{method.Name} 含 ref/out/in 参数，AI 工具无法表达，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            methodMetadata.TryGetValue(method.Name, out (string? ToolName, string? Description) metadata);

            methods.Add(BuildMethod(contractName, method, metadata));
        }

        foreach (string unknown in methodMetadata.Keys.Except(methods.Select(static method => method.Name), StringComparer.Ordinal))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.UnknownMethodMetadata,
                $"契约 {contract.Name} 中不存在方法 {unknown}（来自 [AiMethod]），已忽略该元数据。",
                anchor.Locations.FirstOrDefault()));
        }

        if (methods.Count == 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.ContractWithoutMethods,
                $"契约 {contract.ToDisplayString()} 没有可桥接的实例方法。",
                contract.Locations.FirstOrDefault()));
        }

        string bridgeNamespace = contract.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? GeneratedRootNamespace + "." + ns.ToDisplayString()
            : GeneratedRootNamespace;

        var spec = new ContractSpec(
            InterfaceFullName: contract.ToDisplayString(TypeFormat),
            ContractFullName: contract.ToDisplayString(),
            BridgeNamespace: bridgeNamespace,
            BridgeName: contractName + "Bridge",
            ContractName: contractName,
            Lifetime: lifetime,
            GenerateRegistration: generateRegistration,
            Methods: new EquatableArray<MethodSpec>(methods.ToArray()));

        return new BindingResult(spec, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static Dictionary<string, (string? ToolName, string? Description)> ReadMethodMetadata(
        GeneratorAttributeSyntaxContext context,
        INamedTypeSymbol anchor,
        List<DiagnosticInfo> diagnostics)
    {
        var result = new Dictionary<string, (string? ToolName, string? Description)>(StringComparer.Ordinal);

        INamedTypeSymbol? methodAttributeType = context.SemanticModel.Compilation
            .GetTypeByMetadataName(MethodAttributeMetadataName);

        if (methodAttributeType is null)
        {
            return result;
        }

        foreach (AttributeData attribute in anchor.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, methodAttributeType))
            {
                continue;
            }

            string? methodName = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string
                : null;

            if (string.IsNullOrWhiteSpace(methodName))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.UnknownMethodMetadata,
                    $"{anchor.ToDisplayString()} 上的 [AiMethod] 缺少方法名，已忽略。",
                    anchor.Locations.FirstOrDefault()));
                continue;
            }

            result[methodName!] = (ReadString(attribute, "ToolName"), ReadString(attribute, "Description"));
        }

        return result;
    }

    private static MethodSpec BuildMethod(
        string contractName,
        IMethodSymbol method,
        (string? ToolName, string? Description) metadata)
    {
        var parameters = new List<ParamSpec>();

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            string typeName = parameter.Type.ToDisplayString(TypeFormat);
            if (parameter.IsParams && parameter.Type is IArrayTypeSymbol arrayType)
            {
                typeName = "params " + arrayType.ElementType.ToDisplayString(TypeFormat) + "[]";
            }

            parameters.Add(new ParamSpec(
                TypeName: typeName,
                Name: parameter.Name,
                DefaultValueExpression: FormatDefaultValue(parameter)));
        }

        return new MethodSpec(
            Name: method.Name,
            // FullyQualifiedFormat 会把 void 显示成 global::System.Void（C# 不允许），这里特判
            ReturnTypeName: method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(TypeFormat),
            Parameters: new EquatableArray<ParamSpec>(parameters.ToArray()),
            Description: metadata.Description ?? string.Empty,
            SuggestedToolName: metadata.ToolName ?? BuildSuggestedToolName(contractName, method.Name));
    }

    private static string BuildSuggestedToolName(string contractName, string methodName)
    {
        string bareName = methodName.EndsWith("Async", StringComparison.Ordinal) && methodName.Length > "Async".Length
            ? methodName.Substring(0, methodName.Length - "Async".Length)
            : methodName;

        return ToSnakeCase(contractName) + "_" + ToSnakeCase(bareName);
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new StringBuilder(value.Length + 4);
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string TrimContractPrefix(string interfaceName)
        => interfaceName.Length > 1 && interfaceName[0] == 'I' && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1)
            : interfaceName;

    private static string? ReadString(AttributeData attribute, string propertyName)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, propertyName, StringComparison.Ordinal) &&
                argument.Value.Value is string text &&
                text.Length > 0)
            {
                return text;
            }
        }

        return null;
    }

    private static bool? ReadBoolean(AttributeData attribute, string propertyName)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, propertyName, StringComparison.Ordinal) &&
                argument.Value.Value is bool value)
            {
                return value;
            }
        }

        return null;
    }

    private static string ReadLifetime(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, "Lifetime", StringComparison.Ordinal) &&
                argument.Value.Value is int lifetimeValue)
            {
                return lifetimeValue switch
                {
                    1 => "Scoped",
                    2 => "Singleton",
                    _ => "Transient",
                };
            }
        }

        return "Transient";
    }

    private static string? FormatDefaultValue(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            return null;
        }

        object? value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            return "default";
        }

        if (parameter.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            return "((" + enumType.ToDisplayString(TypeFormat) + ")" +
                   Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + ")";
        }

        switch (value)
        {
            case bool boolean:
                return boolean ? "true" : "false";
            case string text:
                return FormatStringLiteral(text);
            case char character:
                return "'" + EscapeChar(character) + "'";
            case double doubleValue:
                return doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d";
            case float floatValue:
                return floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
            case decimal decimalValue:
                return decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
            case long longValue:
                return longValue.ToString(CultureInfo.InvariantCulture) + "L";
            case ulong unsignedLongValue:
                return unsignedLongValue.ToString(CultureInfo.InvariantCulture) + "UL";
            case uint unsignedIntValue:
                return unsignedIntValue.ToString(CultureInfo.InvariantCulture) + "U";
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    internal static string FormatStringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            builder.Append(EscapeChar(character));
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static string EscapeChar(char character) => character switch
    {
        '\\' => "\\\\",
        '"' => "\\\"",
        '\'' => "\\'",
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        '\0' => "\\0",
        _ => character.ToString(),
    };
}

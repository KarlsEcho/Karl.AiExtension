using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace AI.Extension.Abstractions.Generator.Internal;

/// <summary>
/// 从编译上下文中收集 AI 契约。
/// <para>
/// 关键点：契约接口通常声明在<b>另一个程序集</b>（Demo.Contract）里，
/// 因此不能只用 <c>ForAttributeWithMetadataName</c>（它只扫描当前编译的语法树），
/// 而是扫描「当前编译 + 所有引用了 AI.Extension.Abstractions 的引用程序集」的元数据。
/// 这正是「Demo.MCP 只引 Abstractions，生成器按契约生成基础设施」能够成立的原因。
/// </para>
/// </summary>
internal static class ContractCollector
{
    private const string AiContractAttributeMetadataName = "AI.Extension.AiContractAttribute";
    private const string AiToolAttributeMetadataName = "AI.Extension.AiToolAttribute";
    private const string AiIgnoreAttributeMetadataName = "AI.Extension.AiIgnoreAttribute";
    private const string DescriptionAttributeMetadataName = "System.ComponentModel.DescriptionAttribute";
    private const string AbstractionsAssemblyName = "AI.Extension.Abstractions";
    private const string GeneratedRootNamespace = "AI.Extension.Generated";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static ContractCollection Collect(Compilation compilation, CancellationToken cancellationToken)
    {
        INamedTypeSymbol? contractAttribute = compilation.GetTypeByMetadataName(AiContractAttributeMetadataName);
        if (contractAttribute is null)
        {
            // 没有引用 AI.Extension.Abstractions，视为无契约。
            return ContractCollection.Empty;
        }

        INamedTypeSymbol? toolAttribute = compilation.GetTypeByMetadataName(AiToolAttributeMetadataName);
        INamedTypeSymbol? ignoreAttribute = compilation.GetTypeByMetadataName(AiIgnoreAttributeMetadataName);
        INamedTypeSymbol? descriptionAttribute = compilation.GetTypeByMetadataName(DescriptionAttributeMetadataName);

        var contracts = new List<ContractSpec>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (IAssemblySymbol assembly in GetCandidateAssemblies(compilation))
        {
            foreach (INamedTypeSymbol type in EnumerateTypes(assembly.GlobalNamespace))
            {
                cancellationToken.ThrowIfCancellationRequested();

                AttributeData? contractData = FindAttribute(type, contractAttribute);
                if (contractData is null)
                {
                    continue;
                }

                if (type.TypeKind != TypeKind.Interface)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.AttributeOnNonInterface,
                        $"[AiContract] 标注在 {type.ToDisplayString()} 上，但源生成器只会为 interface 生成基础设施，已跳过。",
                        type.Locations.FirstOrDefault()));
                    continue;
                }

                ContractSpec? spec = BuildContract(
                    type,
                    contractData,
                    toolAttribute,
                    ignoreAttribute,
                    descriptionAttribute,
                    diagnostics);

                if (spec is not null)
                {
                    contracts.Add(spec);
                }
            }
        }

        return new ContractCollection(
            new EquatableArray<ContractSpec>(contracts.ToArray()),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
    }

    private static IEnumerable<IAssemblySymbol> GetCandidateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (string.Equals(assembly.Name, compilation.AssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            // 只扫描引用了 Abstractions 的程序集：系统程序集会被这条规则自然排除。
            bool referencesAbstractions = assembly.Modules.Any(module =>
                module.ReferencedAssemblySymbols.Any(reference =>
                    string.Equals(reference.Name, AbstractionsAssemblyName, StringComparison.Ordinal)));

            if (referencesAbstractions)
            {
                yield return assembly;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol root)
    {
        foreach (INamespaceOrTypeSymbol member in root.GetMembers())
        {
            if (member is INamespaceSymbol childNamespace)
            {
                foreach (INamedTypeSymbol type in EnumerateTypes(childNamespace))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                foreach (INamedTypeSymbol nested in EnumerateTypeAndNested(type))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(INamedTypeSymbol type)
    {
        yield return type;

        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        {
            foreach (INamedTypeSymbol descendant in EnumerateTypeAndNested(nested))
            {
                yield return descendant;
            }
        }
    }

    private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
        => symbol.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));

    private static ContractSpec? BuildContract(
        INamedTypeSymbol contract,
        AttributeData contractData,
        INamedTypeSymbol? toolAttribute,
        INamedTypeSymbol? ignoreAttribute,
        INamedTypeSymbol? descriptionAttribute,
        List<DiagnosticInfo> diagnostics)
    {
        string? explicitName = ReadStringArgument(contractData, "Name") ?? ReadConstructorString(contractData);

        string contractName = explicitName is { Length: > 0 }
            ? explicitName
            : TrimContractPrefix(contract.Name);

        if (contractName.Length == 0)
        {
            contractName = contract.Name;
        }

        string lifetime = ReadLifetime(contractData);
        bool generateRegistration = ReadBooleanArgument(contractData, "GenerateRegistration") ?? true;

        if (contract.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.ContractNotAccessible,
                $"契约 {contract.ToDisplayString()} 的访问级别为 {contract.DeclaredAccessibility}，生成程序集无法引用它，已跳过。",
                contract.Locations.FirstOrDefault()));
            return null;
        }

        string bridgeNamespace = contract.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? GeneratedRootNamespace + "." + ns.ToDisplayString()
            : GeneratedRootNamespace;

        var methods = new List<MethodSpec>();

        foreach (ISymbol member in contract.GetMembers())
        {
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
                continue; // 属性/事件的访问器
            }

            if (ignoreAttribute is not null && FindAttribute(method, ignoreAttribute) is not null)
            {
                continue;
            }

            if (method.IsStatic)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.GenericMethodSkipped,
                    $"契约方法 {contract.Name}.{method.Name} 是静态成员，源生成器只桥接实例方法，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            if (method.TypeParameters.Length > 0)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.GenericMethodSkipped,
                    $"契约方法 {contract.Name}.{method.Name} 是泛型方法，AI 工具无法表达泛型，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            if (method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.GenericMethodSkipped,
                    $"契约方法 {contract.Name}.{method.Name} 含 ref/out/in 参数，AI 工具无法表达，已跳过。",
                    method.Locations.FirstOrDefault()));
                continue;
            }

            MethodSpec methodSpec = BuildMethod(method, contractName, toolAttribute, ignoreAttribute, descriptionAttribute, contract);
            methods.Add(methodSpec);
        }

        if (methods.Count == 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.ContractWithoutMethods,
                $"契约 {contract.ToDisplayString()} 没有可桥接的实例方法。",
                contract.Locations.FirstOrDefault()));
        }

        return new ContractSpec(
            InterfaceFullName: contract.ToDisplayString(TypeFormat),
            ContractFullName: contract.ToDisplayString(),
            BridgeNamespace: bridgeNamespace,
            BridgeName: contractName + "Bridge",
            ContractName: contractName,
            Lifetime: lifetime,
            GenerateRegistration: generateRegistration,
            Methods: new EquatableArray<MethodSpec>(methods.ToArray()));
    }

    private static MethodSpec BuildMethod(
        IMethodSymbol method,
        string contractName,
        INamedTypeSymbol? toolAttributeType,
        INamedTypeSymbol? ignoreAttributeType,
        INamedTypeSymbol? descriptionAttributeType,
        INamedTypeSymbol contract)
    {
        string? description = null;
        string? suggestedName = null;

        if (toolAttributeType is not null && FindAttribute(method, toolAttributeType) is { } toolData)
        {
            description = ReadStringArgument(toolData, "Description") ?? ReadConstructorString(toolData);
            suggestedName = ReadStringArgument(toolData, "Name");
        }

        if (description is null && descriptionAttributeType is not null)
        {
            AttributeData? descriptionData = FindAttribute(method, descriptionAttributeType);
            if (descriptionData is not null)
            {
                description = ReadConstructorString(descriptionData);
            }
        }

        string methodName = method.Name;
        string suggestedToolName = suggestedName is { Length: > 0 }
            ? suggestedName
            : BuildSuggestedToolName(contractName, methodName);

        var parameters = new List<ParamSpec>();
        bool hasOmittedParameter = false;

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            bool omit = ignoreAttributeType is not null
                        && FindAttribute(parameter, ignoreAttributeType) is not null
                        && parameter.HasExplicitDefaultValue;

            if (omit)
            {
                hasOmittedParameter = true;
                continue;
            }

            string typeName = parameter.Type.ToDisplayString(TypeFormat);
            if (parameter.IsParams && parameter.Type is IArrayTypeSymbol arrayType)
            {
                typeName = "params " + arrayType.ElementType.ToDisplayString(TypeFormat) + "[]";
            }

            parameters.Add(new ParamSpec(
                TypeName: typeName,
                Name: parameter.Name,
                DefaultValueExpression: FormatDefaultValue(parameter),
                Omit: hasOmittedParameter));
        }

        return new MethodSpec(
            Name: methodName,
            ReturnTypeName: method.ReturnType.ToDisplayString(TypeFormat),
            Parameters: new EquatableArray<ParamSpec>(parameters.ToArray()),
            Description: description ?? string.Empty,
            SuggestedToolName: suggestedToolName);
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

    private static string? ReadStringArgument(AttributeData attribute, string propertyName)
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

    private static string? ReadConstructorString(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string text)
        {
            return text;
        }

        return null;
    }

    private static bool? ReadBooleanArgument(AttributeData attribute, string propertyName)
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

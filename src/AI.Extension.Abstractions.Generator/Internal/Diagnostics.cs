using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace AI.Extension.Abstractions.Generator.Internal;

/// <summary>可增量缓存的诊断信息（Location 本身不可缓存，因此降到轻量结构）。</summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? CreateFrom(Location? location)
    {
        if (location is null || location.SourceTree is null || location == Location.None)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }
}

/// <summary>生成器诊断清单。</summary>
internal static class Diagnostics
{
    public const string Category = "AI.Extension.Generator";

    public static readonly DiagnosticDescriptor GenericMethodSkipped = new(
        id: "AIEX001",
        title: "契约方法无法桥接：含泛型参数或 ref/out/in 参数",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContractNotAccessible = new(
        id: "AIEX002",
        title: "契约接口对生成程序集不可见",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AttributeOnNonInterface = new(
        id: "AIEX003",
        title: "[AiContract] 只能标注在 interface 上",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContractWithoutMethods = new(
        id: "AIEX004",
        title: "契约没有可桥接的方法",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonMethodMemberIgnored = new(
        id: "AIEX005",
        title: "契约成员被忽略：只桥接普通实例方法",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = new(StringComparer.Ordinal)
    {
        [GenericMethodSkipped.Id] = GenericMethodSkipped,
        [ContractNotAccessible.Id] = ContractNotAccessible,
        [AttributeOnNonInterface.Id] = AttributeOnNonInterface,
        [ContractWithoutMethods.Id] = ContractWithoutMethods,
        [NonMethodMemberIgnored.Id] = NonMethodMemberIgnored,
    };

    public static void Report(SourceProductionContext context, DiagnosticInfo diagnostic)
    {
        if (!ById.TryGetValue(diagnostic.DescriptorId, out DiagnosticDescriptor? descriptor))
        {
            return;
        }

        Location location = diagnostic.Location?.ToLocation() ?? Location.None;
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, diagnostic.Message));
    }
}

internal sealed record DiagnosticInfo(string DescriptorId, string Message, LocationInfo? Location)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, string message, Location? location)
        => new(descriptor.Id, message, LocationInfo.CreateFrom(location));
}

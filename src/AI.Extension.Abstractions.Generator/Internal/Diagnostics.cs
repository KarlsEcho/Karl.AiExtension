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

    public static readonly DiagnosticDescriptor UnsupportedContractMethod = new(
        id: "AIEX001",
        title: "契约方法无法桥接：含泛型参数、ref/out/in 参数或为静态成员",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BindingTargetNotInterface = new(
        id: "AIEX002",
        title: "[AiContractBinding] 的 typeof(...) 必须解析为接口",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContractWithoutMethods = new(
        id: "AIEX003",
        title: "契约没有可桥接的方法",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonMethodMemberIgnored = new(
        id: "AIEX004",
        title: "契约成员被忽略：只桥接普通实例方法",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownMethodMetadata = new(
        id: "AIEX005",
        title: "[AiMethod] 指向了契约中不存在的方法",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateBridge = new(
        id: "AIEX006",
        title: "重复的桥接声明（同名桥接只生成一次）",
        messageFormat: "{0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly Dictionary<string, DiagnosticDescriptor> ById = new(StringComparer.Ordinal)
    {
        [UnsupportedContractMethod.Id] = UnsupportedContractMethod,
        [BindingTargetNotInterface.Id] = BindingTargetNotInterface,
        [ContractWithoutMethods.Id] = ContractWithoutMethods,
        [NonMethodMemberIgnored.Id] = NonMethodMemberIgnored,
        [UnknownMethodMetadata.Id] = UnknownMethodMetadata,
        [DuplicateBridge.Id] = DuplicateBridge,
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

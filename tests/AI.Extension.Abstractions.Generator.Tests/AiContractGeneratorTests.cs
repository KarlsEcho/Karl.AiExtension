using AI.Extension.Abstractions.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AI.Extension.Abstractions.Generator.Tests;

/// <summary>源生成器单测：验证「契约 → 桥接基础设施 + DI 注册」的核心契约。</summary>
public sealed class AiContractGeneratorTests
{
    private const string ContractSource = """
        using AI.Extension;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;

        namespace Demo.Contract;

        [AiContract(Name = "OrderService", Lifetime = AiBridgeLifetime.Singleton)]
        public interface IOrderService
        {
            [AiTool("按关键词检索订单", Name = "order_search")]
            Task<IReadOnlyList<string>> SearchAsync(string? keyword, CancellationToken cancellationToken = default);

            [AiTool("取消订单")]
            Task<string> CancelAsync(string orderId, string reason, CancellationToken cancellationToken = default);

            [AiIgnore]
            void RefreshUi();
        }
        """;

    [Fact]
    public void GeneratesBridgeRegistrationAndCatalog()
    {
        GenerationResult result = RunGenerator(ContractSource);

        AssertNoErrors(result);
        Assert.Contains(result.Sources, source => source.Contains("class OrderServiceBridge", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("AddAiContractBridges", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("\"order_search\"", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("ServiceDescriptor.Singleton", StringComparison.Ordinal));
    }

    [Fact]
    public void IgnoredMemberIsNotBridged()
    {
        GenerationResult result = RunGenerator(ContractSource);

        string bridge = Assert.Single(result.Sources, source => source.Contains("class OrderServiceBridge", StringComparison.Ordinal));
        Assert.DoesNotContain("RefreshUi", bridge, StringComparison.Ordinal);
        Assert.Contains("SearchAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("CancelAsync", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericContractMethodIsSkippedWithWarning()
    {
        const string source = """
            using AI.Extension;
            using System.Threading.Tasks;

            namespace Demo.Contract;

            [AiContract]
            public interface IBadContract
            {
                Task<T> GetAsync<T>(string id);
                Task<string> OkAsync(string id);
            }
            """;

        GenerationResult result = RunGenerator(source);

        AssertNoErrors(result);
        Assert.Contains(result.GeneratedDiagnostics, diagnostic => diagnostic.Id == "AIEX001");
        string bridge = Assert.Single(result.Sources, sourceText => sourceText.Contains("class BadContractBridge", StringComparison.Ordinal));
        Assert.DoesNotContain("GetAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("OkAsync", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedCodeCompiles()
    {
        GenerationResult result = RunGenerator(ContractSource);

        AssertNoErrors(result);
        Assert.Contains(result.Sources, source => source.Contains("Methods { get; }", StringComparison.Ordinal));
    }

    private static void AssertNoErrors(GenerationResult result)
    {
        Assert.True(result.Errors.Count == 0, string.Join(Environment.NewLine, result.Errors));
    }

    private static GenerationResult RunGenerator(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Cast<MetadataReference>()
            .ToList();

        string abstractionsPath = typeof(AiContractAttribute).Assembly.Location;
        if (!references.Any(reference => reference.Display == abstractionsPath))
        {
            references.Add(MetadataReference.CreateFromFile(abstractionsPath));
        }

        // 生成的 registration 依赖 Microsoft.Extensions.DependencyInjection，需要显式加入引用
        string dependencyInjectionPath = typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location;
        if (!references.Any(reference => reference.Display == dependencyInjectionPath))
        {
            references.Add(MetadataReference.CreateFromFile(dependencyInjectionPath));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new AiContractGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out var driverDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();

        var errors = outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        errors.AddRange(driverDiagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString()));

        return new GenerationResult(
            runResult.GeneratedTrees.Select(tree => tree.ToString()).ToList(),
            errors,
            runResult.Diagnostics.ToList());
    }

    private sealed record GenerationResult(
        IReadOnlyList<string> Sources,
        IReadOnlyList<string> Errors,
        IReadOnlyList<Diagnostic> GeneratedDiagnostics);
}



using AI.Extension.Abstractions.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AI.Extension.Abstractions.Generator.Tests;

/// <summary>
/// 源生成器单测：验证「MCP 层声明绑定 → 契约桥接基础设施 + DI 注册」的核心契约，
/// 以及「契约层不被打任何 AI 特性」这一设计约束。
/// </summary>
public sealed class AiContractGeneratorTests
{
    private const string ContractAndBindingSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        using AI.Extension;

        namespace Demo.Contract
        {
            // 纯业务契约：不引用 AI.Extension，不打任何 AI 特性
            public interface IOrderService
            {
                Task<IReadOnlyList<string>> SearchAsync(string? keyword, CancellationToken cancellationToken = default);
                Task<string> CancelAsync(string orderId, string reason, CancellationToken cancellationToken = default);
                void RefreshUi();
            }
        }

        namespace Demo.Mcp
        {
            [AiContractBinding(typeof(Demo.Contract.IOrderService), Lifetime = AiBridgeLifetime.Singleton)]
            [AiMethod(nameof(Demo.Contract.IOrderService.SearchAsync), ToolName = "order_search", Description = "按关键词检索订单")]
            [AiMethod(nameof(Demo.Contract.IOrderService.CancelAsync), Description = "取消订单")]
            internal sealed class OrderServiceBinding;
        }
        """;

    [Fact]
    public void GeneratesBridgeRegistrationAndCatalog()
    {
        GenerationResult result = RunGenerator(ContractAndBindingSource);

        AssertNoErrors(result);
        Assert.Contains(result.Sources, source => source.Contains("class OrderServiceBridge", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("AddAiContractBridges", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("\"order_search\"", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("ServiceDescriptor.Singleton", StringComparison.Ordinal));
    }

    [Fact]
    public void ContractSourceShell_HasNoAiAttributes()
    {
        // 契约层零污染：生成器的输入只来自 MCP 层的绑定声明
        string contractPart = ContractAndBindingSource.Split("namespace Demo.Mcp")[0];
        Assert.DoesNotContain("AiContract", contractPart, StringComparison.Ordinal);
        Assert.DoesNotContain("AiMethod", contractPart, StringComparison.Ordinal);
    }

    [Fact]
    public void BridgesAllContractMethods()
    {
        GenerationResult result = RunGenerator(ContractAndBindingSource);

        string bridge = Assert.Single(result.Sources, source => source.Contains("class OrderServiceBridge", StringComparison.Ordinal));
        Assert.Contains("SearchAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("CancelAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("RefreshUi", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractIsBridgedOnlyWhereDeclared()
    {
        const string source = """
            using System.Threading.Tasks;
            using AI.Extension;

            namespace Demo.Contract
            {
                public interface IUnboundContract
                {
                    Task<string> DoAsync(string id);
                }
            }
            """;

        GenerationResult result = RunGenerator(source);

        AssertNoErrors(result);
        Assert.DoesNotContain(result.Sources, text => text.Contains("UnboundContractBridge", StringComparison.Ordinal));
        Assert.Contains(result.Sources, text => text.Contains("AddAiContractBridges", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericContractMethodIsSkippedWithWarning()
    {
        const string source = """
            using System.Threading.Tasks;
            using AI.Extension;

            namespace Demo.Contract
            {
                public interface IBadContract
                {
                    Task<T> GetAsync<T>(string id);
                    Task<string> OkAsync(string id);
                }
            }

            namespace Demo.Mcp
            {
                [AiContractBinding(typeof(Demo.Contract.IBadContract))]
                internal sealed class BadContractBinding;
            }
            """;

        GenerationResult result = RunGenerator(source);

        AssertNoErrors(result);
        Assert.Contains(result.GeneratedDiagnostics, diagnostic => diagnostic.Id == "AIEX001");
        string bridge = Assert.Single(result.Sources, text => text.Contains("class BadContractBridge", StringComparison.Ordinal));
        Assert.DoesNotContain("GetAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("OkAsync", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void NonInterfaceBindingTargetIsReported()
    {
        const string source = """
            using AI.Extension;

            namespace Demo.Mcp
            {
                [AiContractBinding(typeof(string))]
                internal sealed class BadBinding;
            }
            """;

        GenerationResult result = RunGenerator(source);

        Assert.Contains(result.GeneratedDiagnostics, diagnostic => diagnostic.Id == "AIEX002");
        Assert.DoesNotContain(result.Sources, text => text.Contains("Bridge : global::AI.Extension.IAiContractBridge", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownMethodMetadataIsReported()
    {
        const string source = """
            using System.Threading.Tasks;
            using AI.Extension;

            namespace Demo.Contract
            {
                public interface IOrderService
                {
                    Task<string> GetAsync(string id);
                }
            }

            namespace Demo.Mcp
            {
                [AiContractBinding(typeof(Demo.Contract.IOrderService))]
                [AiMethod("NotExists", ToolName = "nope")]
                internal sealed class OrderServiceBinding;
            }
            """;

        GenerationResult result = RunGenerator(source);

        Assert.Contains(result.GeneratedDiagnostics, diagnostic => diagnostic.Id == "AIEX005");
    }

    [Fact]
    public void GeneratedCodeCompiles()
    {
        GenerationResult result = RunGenerator(ContractAndBindingSource);

        AssertNoErrors(result);
        Assert.Contains(result.Sources, source => source.Contains("Methods { get; }", StringComparison.Ordinal));
        Assert.Contains(result.Sources, source => source.Contains("Demo.Contract.IOrderService", StringComparison.Ordinal));
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

        string abstractionsPath = typeof(AiContractBindingAttribute).Assembly.Location;
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

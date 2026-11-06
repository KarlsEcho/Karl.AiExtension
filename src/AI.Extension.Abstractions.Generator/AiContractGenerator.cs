using AI.Extension.Abstractions.Generator.Internal;
using Microsoft.CodeAnalysis;

namespace AI.Extension.Abstractions.Generator;

/// <summary>
/// AI 契约源生成器。
/// <para>
/// 输入：任意「引用了 AI.Extension.Abstractions」的程序集中，标注了 <c>[AiContract]</c> 的接口。
/// 输出：每个契约一个强类型桥接类 + 一份 DI 注册扩展 + 一份契约清单。
/// </para>
/// <para>
/// 生成的代码不包含任何 MCP 协议细节——协议与 JSON Schema 交给微软 MCP C# SDK
/// （[McpServerTool] / [Description]），本生成器只负责把「契约方法」变成「可被手写封装直接调用的基础设施」。
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AiContractGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ContractCollection> contracts = context.CompilationProvider
            .Select(static (compilation, cancellationToken) =>
                ContractCollector.Collect(compilation, cancellationToken));

        context.RegisterSourceOutput(contracts, static (productionContext, collection) =>
            Emitter.Emit(productionContext, collection));
    }
}

using System.Collections.Generic;
using AI.Extension.Abstractions.Generator.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AI.Extension.Abstractions.Generator;

/// <summary>
/// 契约桥接源生成器。
/// <para>
/// 输入：MCP 层里标注了 <c>[AiContractBinding(typeof(业务契约接口))]</c> 的声明类
/// （可选的 <c>[AiMethod]</c> 补充「契约方法 → 工具名/描述」的追踪元数据）。
/// </para>
/// <para>
/// 输出：每个契约一个强类型桥接类 + 一份 DI 注册扩展 + 一份契约清单。
/// 生成的代码不包含任何 MCP 协议细节——协议与 JSON Schema 交给微软 MCP C# SDK
/// （<c>[McpServerTool]</c> / <c>[Description]</c>）与手写语义封装。
/// </para>
/// <para>
/// 依赖方向：<c>MCP 层 ──► 契约层</c>。契约层不引用本包、不打任何 AI 特性，
/// 因此 AI 能力对业务是纯粹的「外部附加」。
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class AiContractGenerator : IIncrementalGenerator
{
    private const string BindingAttributeMetadataName = "AI.Extension.AiContractBindingAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<BindingResult> bindings = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                BindingAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, cancellationToken) => BindingCollector.Build(attributeContext, cancellationToken));

        context.RegisterSourceOutput(bindings.Collect(), static (productionContext, results) =>
        {
            var contracts = new List<ContractSpec>();
            var diagnostics = new List<DiagnosticInfo>();

            foreach (BindingResult result in results)
            {
                if (result.Spec is not null)
                {
                    contracts.Add(result.Spec);
                }

                diagnostics.AddRange(result.Diagnostics);
            }

            Emitter.Emit(
                productionContext,
                new ContractCollection(
                    new EquatableArray<ContractSpec>(contracts.ToArray()),
                    new EquatableArray<DiagnosticInfo>(diagnostics.ToArray())));
        });
    }
}

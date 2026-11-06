namespace AI.Extension;

/// <summary>
/// 由源生成器写进「包含契约绑定声明」的程序集，指向生成的桥接注册类型。
/// <para>
/// 用途：宿主只需要告诉门面「工具在哪个程序集」，框架就能自动完成契约桥接注册，
/// 不需要宿主手动调用 <c>AddAiContractBridges()</c>。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AiExtensionContractAssemblyAttribute : Attribute
{
    /// <summary>构造程序集标记。</summary>
    /// <param name="bridgeRegistrationType">生成的桥接注册类型（含 <c>AddAiContractBridges</c> 静态方法）。</param>
    public AiExtensionContractAssemblyAttribute(Type bridgeRegistrationType)
        => BridgeRegistrationType = bridgeRegistrationType;

    /// <summary>生成的桥接注册类型。</summary>
    public Type BridgeRegistrationType { get; }
}

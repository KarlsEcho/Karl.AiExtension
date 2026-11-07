namespace Demo.MCP;

/// <summary>
/// 工具调用的统一返回结构：让 AI 拿到「成功/失败 + 可读原因」，
/// 而不是把 .NET 异常原样抛给模型（异常应被封装层转成工具错误）。
/// </summary>
/// <typeparam name="T">成功时的载荷类型。</typeparam>
/// <param name="Success">是否成功。</param>
/// <param name="Value">成功时的数据。</param>
/// <param name="Error">失败时的可读原因（供 AI 自我纠正）。</param>
public sealed record ToolOutcome<T>(bool Success, T? Value, string? Error)
{
    /// <summary>构造成功结果。</summary>
    public static ToolOutcome<T> Ok(T value) => new(true, value, null);

    /// <summary>构造失败结果。</summary>
    public static ToolOutcome<T> Fail(string error) => new(false, default, error);
}

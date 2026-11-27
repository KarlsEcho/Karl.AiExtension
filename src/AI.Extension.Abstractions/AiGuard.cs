namespace AI.Extension;

/// <summary>
/// 轻量参数校验：替代 .NET 6+ 的 <c>ArgumentNullException.ThrowIfNull</c> 等 API，
/// 让同一份源码可以在 net48 与 net10 上编译。
/// </summary>
public static class AiGuard
{
    /// <summary>参数为 null 时抛出 <see cref="ArgumentNullException"/>。</summary>
    public static void NotNull(object? argument, string? parameterName = null)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(parameterName);
        }
    }

    /// <summary>字符串为 null/空白时抛出 <see cref="ArgumentException"/>。</summary>
    public static void NotNullOrWhiteSpace(string? argument, string? parameterName = null)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            throw new ArgumentException("值不能为 null 或空白。", parameterName);
        }
    }
}

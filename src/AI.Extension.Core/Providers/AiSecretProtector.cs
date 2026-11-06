using System.Security.Cryptography;
using System.Text;

namespace AI.Extension.Providers;

/// <summary>API Key 的静态保护抽象（持久化时使用）。</summary>
public interface IAiSecretProtector
{
    /// <summary>保护明文；空值返回空字符串。</summary>
    string Protect(string? value);

    /// <summary>还原明文；无法还原时返回 null。</summary>
    string? Unprotect(string? value);
}

/// <summary>
/// 默认实现：Windows 上用 DPAPI（当前用户作用域）加密，其他平台降级为 Base64 混淆。
/// <para>
/// 降级模式只是「不明文直存」，不提供真正的机密性；跨平台产品应替换为平台密钥链实现。
/// </para>
/// </summary>
public sealed class DpapiSecretProtector : IAiSecretProtector
{
    private const string DpapiPrefix = "dpapi:";
    private const string ObfuscatedPrefix = "plain:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AI.Extension.Providers.v1");

    /// <inheritdoc />
    public string Protect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(value);

        if (!OperatingSystem.IsWindows())
        {
            return ObfuscatedPrefix + Convert.ToBase64String(plainBytes);
        }

        byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(cipherBytes);
    }

    /// <inheritdoc />
    public string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            if (value.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                if (!OperatingSystem.IsWindows())
                {
                    return null;
                }

                byte[] cipherBytes = Convert.FromBase64String(value.Substring(DpapiPrefix.Length));
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }

            if (value.StartsWith(ObfuscatedPrefix, StringComparison.Ordinal))
            {
                byte[] plainBytes = Convert.FromBase64String(value.Substring(ObfuscatedPrefix.Length));
                return Encoding.UTF8.GetString(plainBytes);
            }

            // 兼容手工写入的明文（例如用户直接编辑配置文件）
            return value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

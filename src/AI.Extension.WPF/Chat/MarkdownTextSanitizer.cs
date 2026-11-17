using System.Text.RegularExpressions;

namespace AI.Extension.WPF.Chat;

/// <summary>
/// Markdown 文本安全预处理：
/// - 移除远程图片（避免打开聊天记录时自动发起网络请求）；
/// - 代码围栏内的内容不做处理；
/// - 原始 HTML 不在清洗范围内（MdXaml 不执行 HTML，会按文本显示）。
/// </summary>
internal static class MarkdownTextSanitizer
{
    private static readonly Regex RemoteInlineImage = new(
        @"!\[(?<alt>[^\]]*)\]\(\s*<?https?://[^)\s>]+>?(?:\s+""[^""]*"")?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemoteReferenceDefinition = new(
        @"^\s*\[[^\]]+\]:\s*<?https?://\S+>?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>清洗 Markdown 文本。</summary>
    public static string Sanitize(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        string normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');

        bool inFence = false;
        string fenceMarker = string.Empty;

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();

            if (!inFence && (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal)))
            {
                inFence = true;
                fenceMarker = trimmed[..3];
                continue;
            }

            if (inFence)
            {
                if (trimmed.StartsWith(fenceMarker, StringComparison.Ordinal))
                {
                    inFence = false;
                }

                continue;
            }

            if (RemoteReferenceDefinition.IsMatch(lines[i]))
            {
                lines[i] = string.Empty;
                continue;
            }

            lines[i] = RemoteInlineImage.Replace(
                lines[i],
                match => string.IsNullOrWhiteSpace(match.Groups["alt"].Value)
                    ? "（远程图片已禁用）"
                    : $"{match.Groups["alt"].Value}（远程图片已禁用）");
        }

        return string.Join("\n", lines);
    }
}

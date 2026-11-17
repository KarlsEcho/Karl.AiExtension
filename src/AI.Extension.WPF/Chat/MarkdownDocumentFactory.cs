using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MdXaml;

namespace AI.Extension.WPF.Chat;

/// <summary>把 Markdown 转成 WPF FlowDocument，并统一处理链接安全策略。</summary>
internal static class MarkdownDocumentFactory
{
    /// <summary>创建 FlowDocument。</summary>
    public static FlowDocument Create(string markdown)
    {
        var engine = new MdXaml.Markdown
        {
            OnHyperLinkClicked = SafeOpenLink,
            HyperlinkCommand = SafeLinkCommand.Instance,
        };

        FlowDocument document = engine.Transform(MarkdownTextSanitizer.Sanitize(markdown));
        document.PagePadding = new Thickness(0);
        return document;
    }

    private static void SafeOpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器不是致命错误，静默失败即可
        }
    }

    private sealed class SafeLinkCommand : ICommand
    {
        public static readonly SafeLinkCommand Instance = new();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            if (parameter is string url)
            {
                SafeOpenLink(url);
            }
            else if (parameter is Uri uri)
            {
                SafeOpenLink(uri.AbsoluteUri);
            }
        }
    }
}

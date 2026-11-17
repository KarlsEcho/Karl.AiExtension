using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Documents;
using AI.Extension.WPF.Chat;

namespace Demo.Tests;

/// <summary>Markdown 渲染与安全清洗测试。</summary>
public class MarkdownRenderingTests
{
    [Fact]
    public void 远程图片会被移除_本地图片保留()
    {
        string sanitized = MarkdownTextSanitizer.Sanitize(
            "![logo](https://example.com/a.png)\n\n![local](images/a.png)");

        Assert.DoesNotContain("http", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("远程图片已禁用", sanitized);
        Assert.Contains("![local](images/a.png)", sanitized);
    }

    [Fact]
    public void 代码块内的图片语法不处理()
    {
        const string markdown = "```\n![x](https://example.com/a.png)\n```";

        string sanitized = MarkdownTextSanitizer.Sanitize(markdown);

        Assert.Contains("https://example.com/a.png", sanitized);
    }

    [Fact]
    public void 远程图片引用定义会被移除()
    {
        string sanitized = MarkdownTextSanitizer.Sanitize(
            "![logo][ref]\n\n[ref]: https://example.com/a.png");

        Assert.DoesNotContain("https://example.com/a.png", sanitized);
    }

    [Fact]
    public void Markdown转FlowDocument()
    {
        RunOnSta(() =>
        {
            FlowDocument document = MarkdownDocumentFactory.Create(
                "# 标题\n\n- 第一项\n- 第二项\n\n```cs\nvar x = 1;\n```");

            string text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            Assert.Contains("标题", text);
            Assert.Contains("第一项", text);

            // 代码块在 MdXaml 中由 BlockUIContainer + AvalonEdit 承载，不在 TextRange 里
            Assert.Contains(document.Blocks, block => block is BlockUIContainer);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}


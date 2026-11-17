using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace AI.Extension.WPF.Chat;

/// <summary>
/// 单条消息的 Markdown 渲染器：
/// 用 MdXaml 引擎把 Markdown 转成 FlowDocument，再放进只读 RichTextBox，
/// 保留 WPF 原生的选中复制能力；流式追加时按节流刷新。
/// </summary>
public sealed class MarkdownMessageView : ContentControl
{
    /// <summary>Markdown 文本。</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownMessageView),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    private readonly RichTextBox _viewer;
    private readonly DispatcherTimer _updateTimer;
    private string _pending = string.Empty;

    /// <summary>渲染完成（含节流刷新）后触发，供外层滚动到底部。</summary>
    public event EventHandler? Rendered;

    /// <summary>构造。</summary>
    public MarkdownMessageView()
    {
        _viewer = new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,
            IsDocumentEnabled = true,
            IsTabStop = false,
            Focusable = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Content = _viewer;
        Focusable = false;
        IsTabStop = false;

        _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _updateTimer.Tick += (_, _) => Flush();

        Unloaded += (_, _) => _updateTimer.Stop();
    }

    /// <summary>Markdown 文本。</summary>
    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (MarkdownMessageView)d;
        view._pending = e.NewValue as string ?? string.Empty;

        if (view._pending.Length == 0)
        {
            view._viewer.Document = new FlowDocument();
            return;
        }

        if (!view._updateTimer.IsEnabled)
        {
            view._updateTimer.Start();
        }
    }

    private void Flush()
    {
        _updateTimer.Stop();
        _viewer.Document = MarkdownDocumentFactory.Create(_pending);
        Rendered?.Invoke(this, EventArgs.Empty);
    }
}

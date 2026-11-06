using System.Windows;
using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using Demo.ViewModel;

namespace Demo.View;

/// <summary>主窗口：左侧传统业务界面，右侧 AI 外壳。视图只负责组装，业务在 ViewModel。</summary>
public partial class MainWindow : Window
{
    private readonly OrdersViewModel _orders;

    /// <summary>构造主窗口（依赖由 DI 注入）。</summary>
    public MainWindow(
        OrdersViewModel orders,
        ChatPanelViewModel chat,
        AiDiagnosticsViewModel diagnostics)
    {
        InitializeComponent();

        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        DataContext = orders;

        ChatSurface.DataContext = chat;
        DiagnosticsSurface.DataContext = diagnostics;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _orders.LoadAsync();
    }
}

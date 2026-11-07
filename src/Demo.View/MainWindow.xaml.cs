using System.Windows;
using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;
using Demo.ViewModel;

namespace Demo.View;

/// <summary>主窗口：左侧传统业务界面，右侧 AI 外壳（对话 / 模型与供应商 / 工具与契约）。视图只负责组装，业务在 ViewModel。</summary>
public partial class MainWindow : Window
{
    private readonly OrdersViewModel _orders;
    private readonly ChatTabsViewModel _chatTabs;

    /// <summary>构造主窗口（依赖由 DI 注入）。</summary>
    public MainWindow(
        OrdersViewModel orders,
        ChatTabsViewModel chatTabs,
        ProviderManagerViewModel providers,
        AiDiagnosticsViewModel diagnostics)
    {
        InitializeComponent();

        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _chatTabs = chatTabs ?? throw new ArgumentNullException(nameof(chatTabs));
        DataContext = orders;

        ChatSurface.DataContext = chatTabs;
        ProvidersSurface.DataContext = providers;
        DiagnosticsSurface.DataContext = diagnostics;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _orders.LoadAsync();
        await _chatTabs.InitializeAsync();
    }
}

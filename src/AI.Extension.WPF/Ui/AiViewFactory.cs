using System.Windows;
using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Extension.WPF.Ui;

/// <summary>默认视图工厂：按 <see cref="AiExtensionUiOptions"/> 的槽位类型创建视图。</summary>
public sealed class AiViewFactory : IAiViewFactory
{
    private readonly IServiceProvider _services;
    private readonly AiExtensionUiOptions _options;

    /// <summary>构造工厂。</summary>
    public AiViewFactory(IServiceProvider services, AiExtensionUiOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public FrameworkElement CreateChatShell(ChatTabsViewModel viewModel)
        => Create(_options.ChatShellView ?? typeof(AiChatPanel), viewModel);

    /// <inheritdoc />
    public FrameworkElement CreateConversation(ChatPanelViewModel viewModel)
        => Create(_options.ConversationView ?? typeof(ChatConversationView), viewModel);

    /// <inheritdoc />
    public FrameworkElement CreateProviderManager(ProviderManagerViewModel viewModel)
        => Create(_options.ProviderManagerView ?? typeof(AiProviderManagerPanel), viewModel);

    /// <inheritdoc />
    public FrameworkElement CreateDiagnostics(AiDiagnosticsViewModel viewModel)
        => Create(_options.DiagnosticsView ?? typeof(AiDiagnosticsPanel), viewModel);

    private FrameworkElement Create<TViewModel>(Type viewType, TViewModel viewModel)
    {
        if (!typeof(FrameworkElement).IsAssignableFrom(viewType))
        {
            throw new InvalidOperationException(
                $"AI UI 视图 {viewType.FullName} 必须是 WPF FrameworkElement（通常继承 UserControl）。");
        }

        if (!typeof(IAiView<TViewModel>).IsAssignableFrom(viewType))
        {
            throw new InvalidOperationException(
                $"AI UI 视图 {viewType.FullName} 必须实现 IAiView<{typeof(TViewModel).Name}>。");
        }

        object instance = _services.GetService(viewType)
            ?? ActivatorUtilities.CreateInstance(_services, viewType);

        if (instance is not IAiView<TViewModel> view)
        {
            throw new InvalidOperationException(
                $"AI UI 视图 {viewType.FullName} 无法转换为 IAiView<{typeof(TViewModel).Name}>。");
        }

        view.Attach(viewModel);

        var element = (FrameworkElement)instance;
        if (element.DataContext is null)
        {
            element.DataContext = viewModel;
        }

        return element;
    }
}

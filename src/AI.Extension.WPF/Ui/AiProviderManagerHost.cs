using System.Windows.Controls;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;

namespace AI.Extension.WPF.Ui;

/// <summary>模型与供应商视图宿主：DataContext 绑定 <see cref="ProviderManagerViewModel"/>。</summary>
public sealed class AiProviderManagerHost : ContentControl
{
    /// <summary>构造宿主。</summary>
    public AiProviderManagerHost()
    {
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not ProviderManagerViewModel viewModel)
        {
            Content = null;
            return;
        }

        Content = viewModel.ViewFactory is { } factory
            ? factory.CreateProviderManager(viewModel)
            : new AiProviderManagerPanel { DataContext = viewModel };
    }
}

/// <summary>工具与契约视图宿主：DataContext 绑定 <see cref="AiDiagnosticsViewModel"/>。</summary>
public sealed class AiDiagnosticsHost : ContentControl
{
    /// <summary>构造宿主。</summary>
    public AiDiagnosticsHost()
    {
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not AiDiagnosticsViewModel viewModel)
        {
            Content = null;
            return;
        }

        Content = viewModel.ViewFactory is { } factory
            ? factory.CreateDiagnostics(viewModel)
            : new AiDiagnosticsPanel { DataContext = viewModel };
    }
}

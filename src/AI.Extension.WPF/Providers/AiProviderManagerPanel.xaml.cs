using System.Windows.Controls;
using AI.Extension.WPF.Ui;

namespace AI.Extension.WPF.Providers;

/// <summary>「模型与供应商」管理面板。</summary>
public partial class AiProviderManagerPanel : UserControl, IAiView<ProviderManagerViewModel>
{
    /// <inheritdoc />
    public void Attach(ProviderManagerViewModel viewModel) => DataContext = viewModel;

    /// <summary>构造面板。</summary>
    public AiProviderManagerPanel()
    {
        InitializeComponent();
    }
}


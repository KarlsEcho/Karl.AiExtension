using System.Windows.Controls;
using AI.Extension.WPF.Ui;

namespace AI.Extension.WPF.Diagnostics;

/// <summary>诊断面板：展示契约桥接、MCP 工具与 UI 上下文日志。</summary>
public partial class AiDiagnosticsPanel : UserControl, IAiView<AiDiagnosticsViewModel>
{
    /// <inheritdoc />
    public void Attach(AiDiagnosticsViewModel viewModel) => DataContext = viewModel;

    /// <summary>构造面板。</summary>
    public AiDiagnosticsPanel()
    {
        InitializeComponent();
    }
}


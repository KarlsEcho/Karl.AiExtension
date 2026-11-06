using System.Collections.ObjectModel;
using AI.Extension.Mcp;
using AI.Extension.Mvvm;
using ModelContextProtocol.Client;

namespace AI.Extension.WPF.Diagnostics;

/// <summary>契约清单的一行。</summary>
public sealed record ContractRow(string ContractName, string ContractFullName, string MethodList, int MethodCount);

/// <summary>MCP 工具清单的一行。</summary>
public sealed record ToolRow(string Name, string Description);

/// <summary>
/// 诊断面板视图模型：把「源生成器产出的契约」和「MCP Server 实际暴露的工具」并列展示，
/// 用来直观验证 Contract → 生成器 → 工具 这条链路是对齐的。
/// </summary>
public sealed class AiDiagnosticsViewModel : ObservableObject
{
    private readonly AiContractCatalog _catalog;
    private readonly McpServerBridge _bridge;
    private readonly IUiActivityTracker _activityTracker;

    /// <summary>构造诊断视图模型。</summary>
    public AiDiagnosticsViewModel(AiContractCatalog catalog, McpServerBridge bridge, IUiActivityTracker activityTracker)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));

        RefreshCommand = new RelayCommand(_ => Refresh());
        Refresh();
    }

    /// <summary>契约清单。</summary>
    public ObservableCollection<ContractRow> Contracts { get; } = new();

    /// <summary>MCP 工具清单。</summary>
    public ObservableCollection<ToolRow> Tools { get; } = new();

    /// <summary>UI 上下文日志（最近的界面活动）。</summary>
    public ObservableCollection<UiActivityEntry> Activities { get; } = new();

    /// <summary>刷新命令。</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>重新读取契约、工具与活动日志。</summary>
    public void Refresh()
    {
        Contracts.Clear();
        foreach (AiContractDescriptor contract in _catalog.Contracts)
        {
            Contracts.Add(new ContractRow(
                contract.ContractName,
                contract.ContractFullName,
                string.Join("、", contract.Methods.Select(static method => method.MethodName)),
                contract.Methods.Count));
        }

        Tools.Clear();
        foreach (McpClientTool tool in _bridge.Tools)
        {
            Tools.Add(new ToolRow(tool.Name, tool.Description ?? string.Empty));
        }

        Activities.Clear();
        foreach (UiActivityEntry entry in _activityTracker.Snapshot(30).Reverse())
        {
            Activities.Add(entry);
        }
    }
}

using System.ComponentModel;
using System.Collections.ObjectModel;
using AI.Extension.Mcp;
using AI.Extension.Mvvm;
using AI.Extension.WPF.Mcp;
using ModelContextProtocol.Client;

namespace AI.Extension.WPF.Diagnostics;

/// <summary>契约清单的一行。</summary>
public sealed record ContractRow(string ContractName, string ContractFullName, string MethodList, int MethodCount);

/// <summary>MCP 工具清单的一行。</summary>
public sealed record ToolRow(string Name, string Description);

/// <summary>
/// 诊断面板视图模型：把「源生成器产出的契约」和「MCP Server 实际暴露的工具」并列展示，
/// 用来直观验证 Contract → 生成器 → 工具 这条链路是对齐的；同时提供「对外暴露 MCP」的开关。
/// </summary>
public sealed class AiDiagnosticsViewModel : ObservableObject
{
    private readonly AiContractCatalog _catalog;
    private readonly McpServerBridge _bridge;
    private readonly IUiActivityTracker _activityTracker;
    private readonly IMcpHttpExposureService _exposure;

    private string _exposurePortText = "5199";
    private bool _isExposing;
    private bool _isExposureBusy;
    private string _exposureStatusText = "默认关闭：开启后本机 harness 可通过 URL 访问同一套工具。";
    private string? _exposureUrl;

    /// <summary>构造诊断视图模型。</summary>
    public AiDiagnosticsViewModel(
        AiContractCatalog catalog,
        McpServerBridge bridge,
        IUiActivityTracker activityTracker,
        IMcpHttpExposureService exposure)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _exposure = exposure ?? throw new ArgumentNullException(nameof(exposure));

        RefreshCommand = new RelayCommand(_ => Refresh());
        ToggleExposureCommand = new AsyncRelayCommand(_ => ToggleExposureAsync(), _ => !IsExposureBusy);

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

    /// <summary>开启/关闭对外暴露的命令。</summary>
    public AsyncRelayCommand ToggleExposureCommand { get; }

    /// <summary>对外暴露的端口（仅在未开启时可编辑）。</summary>
    public string ExposurePortText
    {
        get => _exposurePortText;
        set => SetProperty(ref _exposurePortText, value);
    }

    /// <summary>是否已开启对外暴露。</summary>
    public bool IsExposing
    {
        get => _isExposing;
        private set
        {
            if (SetProperty(ref _isExposing, value))
            {
                OnPropertyChanged(nameof(ExposureButtonText));
                OnPropertyChanged(nameof(CanEditExposurePort));
            }
        }
    }

    /// <summary>开关按钮文案。</summary>
    public string ExposureButtonText => IsExposing ? "关闭暴露" : "开启暴露";

    /// <summary>是否允许编辑端口。</summary>
    public bool CanEditExposurePort => !IsExposing;

    /// <summary>是否正在开启/关闭。</summary>
    public bool IsExposureBusy
    {
        get => _isExposureBusy;
        private set
        {
            if (SetProperty(ref _isExposureBusy, value))
            {
                ToggleExposureCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>暴露状态文案。</summary>
    public string ExposureStatusText
    {
        get => _exposureStatusText;
        private set => SetProperty(ref _exposureStatusText, value);
    }

    /// <summary>开启后的访问地址。</summary>
    public string? ExposureUrl
    {
        get => _exposureUrl;
        private set => SetProperty(ref _exposureUrl, value);
    }

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

        IsExposing = _exposure.IsRunning;
        ExposureUrl = _exposure.EndpointUrl;
    }

    private async Task ToggleExposureAsync()
    {
        if (IsExposureBusy)
        {
            return;
        }

        IsExposureBusy = true;
        try
        {
            if (_exposure.IsRunning)
            {
                ExposureStatusText = "正在关闭…";
                await _exposure.StopAsync().ConfigureAwait(true);
                IsExposing = false;
                ExposureUrl = null;
                ExposureStatusText = "已关闭（本机 harness 将无法连接）。";
                return;
            }

            if (!int.TryParse(ExposurePortText?.Trim(), out int port) || port is < 1 or > 65535)
            {
                ExposureStatusText = "端口不正确：请输入 1-65535 之间的整数。";
                return;
            }

            ExposureStatusText = $"正在 {port} 端口开启…";
            string url = await _exposure.StartAsync(port).ConfigureAwait(true);
            IsExposing = true;
            ExposureUrl = url;
            ExposureStatusText = "已开启（仅本机可访问）：把下面的 URL 填到 harness 的 MCP 配置里。";
        }
        catch (Exception exception)
        {
            IsExposing = false;
            ExposureUrl = null;
            ExposureStatusText = "开启失败：" + exception.Message;
        }
        finally
        {
            IsExposureBusy = false;
        }
    }
}


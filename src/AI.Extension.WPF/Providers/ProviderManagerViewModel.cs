using System.Collections.ObjectModel;
using AI.Extension.Mvvm;
using AI.Extension.WPF.Ui;
using AI.Extension.Providers;

namespace AI.Extension.WPF.Providers;

/// <summary>WPF「模型与供应商」面板的视图模型：增删改查 + 测试连接 + 拉取模型 + 切换当前模型。</summary>
public sealed class ProviderManagerViewModel : ObservableObject
{
    private readonly IAiProviderManager _manager;
    private readonly IUiDispatcher _uiDispatcher;

    private ProviderItemViewModel? _selectedProvider;
    private string _statusText = "首次运行已写入内置供应商预设；填好 API Key 并启用后即可切换。";
    private bool _isBusy;
    private string _activeModelText;

    /// <summary>构造视图模型。</summary>
    public ProviderManagerViewModel(
        IAiProviderManager manager,
        IUiDispatcher uiDispatcher,
        IAiViewFactory? viewFactory = null)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _activeModelText = manager.ActiveModelLabel;
        ViewFactory = viewFactory;

        AddCommand = new RelayCommand(_ => AddProvider());
        RemoveCommand = new AsyncRelayCommand(_ => RemoveAsync(), _ => SelectedProvider is not null);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => SelectedProvider is not null);
        TestCommand = new AsyncRelayCommand(_ => TestAsync(), _ => SelectedProvider is not null && !IsBusy);
        DiscoverCommand = new AsyncRelayCommand(_ => DiscoverAsync(), _ => SelectedProvider is not null && !IsBusy);
        SetActiveCommand = new AsyncRelayCommand(_ => SetActiveAsync(), _ => SelectedProvider is not null && !IsBusy);
        ClearActiveCommand = new AsyncRelayCommand(_ => ClearActiveAsync(), _ => !IsBusy);
        RefreshCommand = new RelayCommand(_ => Refresh());

        _manager.Changed += OnManagerChanged;
        Refresh();
    }

    /// <summary>供应商列表。</summary>
    public ObservableCollection<ProviderItemViewModel> Providers { get; } = new();

    /// <summary>当前选中的供应商（编辑对象）。</summary>
    public ProviderItemViewModel? SelectedProvider
    {
        get => _selectedProvider;
        set
        {
            if (SetProperty(ref _selectedProvider, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
                TestCommand.RaiseCanExecuteChanged();
                DiscoverCommand.RaiseCanExecuteChanged();
                SetActiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>视图工厂（用于替换供应商面板视图；为空时用内置视图）。</summary>
    public IAiViewFactory? ViewFactory { get; }

    /// <summary>当前生效模型（供应商 · 模型）。</summary>
    public string ActiveModelText
    {
        get => _activeModelText;
        private set => SetProperty(ref _activeModelText, value);
    }

    /// <summary>状态栏文案。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>是否正在执行网络操作。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                TestCommand.RaiseCanExecuteChanged();
                DiscoverCommand.RaiseCanExecuteChanged();
                SetActiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>新增供应商（先进入编辑状态，保存后落盘）。</summary>
    public RelayCommand AddCommand { get; }

    /// <summary>删除选中供应商。</summary>
    public AsyncRelayCommand RemoveCommand { get; }

    /// <summary>保存选中供应商。</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>测试连接。</summary>
    public AsyncRelayCommand TestCommand { get; }

    /// <summary>从端点拉取模型列表。</summary>
    public AsyncRelayCommand DiscoverCommand { get; }

    /// <summary>把选中供应商的默认模型设为当前模型。</summary>
    public AsyncRelayCommand SetActiveCommand { get; }

    /// <summary>清除当前模型（回到离线状态）。</summary>
    public AsyncRelayCommand ClearActiveCommand { get; }

    /// <summary>刷新列表。</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>从管理器重新加载列表。</summary>
    public void Refresh()
    {
        string? selectedId = SelectedProvider?.Id;
        Providers.Clear();

        foreach (AiProviderDefinition provider in _manager.Providers)
        {
            bool isActive = string.Equals(_manager.ActiveProvider?.Id, provider.Id, StringComparison.Ordinal);
            Providers.Add(ProviderItemViewModel.From(provider, isActive));
        }

        SelectedProvider = Providers.FirstOrDefault(item => item.Id == selectedId) ?? Providers.FirstOrDefault();
        ActiveModelText = _manager.ActiveModelLabel;
    }

    private void AddProvider()
    {
        string id = "custom-" + Guid.NewGuid().ToString("N")[..6];
        var provider = new AiProviderDefinition
        {
            Id = id,
            DisplayName = "自定义供应商",
            Endpoint = "https://",
            ApiKey = string.Empty,
            Enabled = false,
            Models = [new AiModelDefinition { Id = "模型-A" }],
            DefaultModelId = "模型-A",
        };

        var item = ProviderItemViewModel.From(provider, isActive: false);
        Providers.Add(item);
        SelectedProvider = item;
        StatusText = "已新增供应商：填写名称、端点、API Key 与模型后点「保存」。";
    }

    private async Task SaveAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        try
        {
            ProviderItemViewModel item = SelectedProvider;
            AiProviderDefinition saved = await _manager.SaveAsync(item.ToDefinition()).ConfigureAwait(true);
            StatusText = $"已保存供应商「{saved.DisplayName}」。";
            Refresh();
        }
        catch (Exception exception)
        {
            StatusText = "保存失败：" + exception.Message;
        }
    }

    private async Task RemoveAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        try
        {
            string displayName = SelectedProvider.DisplayName;
            await _manager.RemoveAsync(SelectedProvider.Id).ConfigureAwait(true);
            StatusText = $"已删除供应商「{displayName}」。";
            Refresh();
        }
        catch (Exception exception)
        {
            StatusText = "删除失败：" + exception.Message;
        }
    }

    private async Task TestAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在测试连接…";
        try
        {
            bool ok = await _manager.TestConnectionAsync(SelectedProvider.Id).ConfigureAwait(true);
            StatusText = ok
                ? "连接成功：端点可访问且已返回模型列表。"
                : "连接失败：请检查 Endpoint、API Key 与网络（或该端点不支持 /models）。";
        }
        catch (Exception exception)
        {
            StatusText = "测试失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DiscoverAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在拉取模型列表…";
        try
        {
            IReadOnlyList<string> models = await _manager.DiscoverModelsAsync(SelectedProvider.Id).ConfigureAwait(true);
            if (models.Count == 0)
            {
                StatusText = "端点没有返回模型列表。";
                return;
            }

            SelectedProvider.ModelsText = string.Join(Environment.NewLine, models);
            SelectedProvider.DefaultModelId = models[0];
            StatusText = $"已拉取 {models.Count} 个模型，记得点「保存」。";
        }
        catch (Exception exception)
        {
            StatusText = "拉取失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SetActiveAsync()
    {
        if (SelectedProvider is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            string modelId = SelectedProvider.ResolveDefaultModelId();
            await _manager.SetActiveAsync(SelectedProvider.Id, modelId).ConfigureAwait(true);
            StatusText = $"当前模型已切换为：{_manager.ActiveModelLabel}";
            Refresh();
        }
        catch (Exception exception)
        {
            StatusText = "切换失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearActiveAsync()
    {
        IsBusy = true;
        try
        {
            await _manager.SetActiveAsync(null, null).ConfigureAwait(true);
            StatusText = "已清除当前模型，AI 将使用离线兜底客户端。";
            Refresh();
        }
        catch (Exception exception)
        {
            StatusText = "清除失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void OnManagerChanged(object? sender, EventArgs e)
    {
        try
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                Refresh();
                return Task.CompletedTask;
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 窗口关闭时忽略
        }
    }
}

/// <summary>供应商列表/编辑区的一行。</summary>
public sealed class ProviderItemViewModel : ObservableObject
{
    private string _displayName;
    private string _endpoint;
    private string _apiKey;
    private bool _enabled;
    private string _modelsText;
    private string _defaultModelId;
    private bool _isActive;

    private ProviderItemViewModel(AiProviderDefinition definition, bool isActive)
    {
        Id = definition.Id;
        RequiresApiKey = definition.RequiresApiKey;
        Notes = definition.Notes ?? string.Empty;
        _displayName = definition.DisplayName;
        _endpoint = definition.Endpoint;
        _apiKey = definition.ApiKey ?? string.Empty;
        _enabled = definition.Enabled;
        _modelsText = string.Join(Environment.NewLine, definition.Models.Select(static model =>
            string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.Id + "|" + model.DisplayName));
        _defaultModelId = definition.DefaultModelId ?? definition.Models.FirstOrDefault()?.Id ?? string.Empty;
        _isActive = isActive;
    }

    /// <summary>从配置构造。</summary>
    public static ProviderItemViewModel From(AiProviderDefinition definition, bool isActive)
        => new(definition, isActive);

    /// <summary>供应商 Id（不可编辑）。</summary>
    public string Id { get; }

    /// <summary>是否需要 API Key。</summary>
    public bool RequiresApiKey { get; }

    /// <summary>备注（只读）。</summary>
    public string Notes { get; }

    /// <summary>显示名。</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    /// <summary>OpenAI 兼容端点。</summary>
    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    /// <summary>API Key（内存明文，落盘时加密）。</summary>
    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    /// <summary>是否启用。</summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    /// <summary>模型列表（每行一个：<c>模型Id</c> 或 <c>模型Id|显示名</c>）。</summary>
    public string ModelsText
    {
        get => _modelsText;
        set => SetProperty(ref _modelsText, value);
    }

    /// <summary>默认模型 Id。</summary>
    public string DefaultModelId
    {
        get => _defaultModelId;
        set => SetProperty(ref _defaultModelId, value);
    }

    /// <summary>是否为当前生效供应商。</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(ActiveBadge));
            }
        }
    }

    /// <summary>列表中的「当前」标记。</summary>
    public string ActiveBadge => IsActive ? "当前" : string.Empty;

    /// <summary>解析模型列表文本。</summary>
    public IReadOnlyList<AiModelDefinition> ParseModels()
        => ModelsText
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line => line.Length > 0)
            .Select(static line =>
            {
                string[] parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
                return new AiModelDefinition
                {
                    Id = parts[0],
                    DisplayName = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null,
                };
            })
            .ToArray();

    /// <summary>解析默认模型；不在列表里时取第一个。</summary>
    public string ResolveDefaultModelId()
    {
        IReadOnlyList<AiModelDefinition> models = ParseModels();
        return models.Any(model => string.Equals(model.Id, DefaultModelId, StringComparison.OrdinalIgnoreCase))
            ? DefaultModelId
            : models.FirstOrDefault()?.Id ?? string.Empty;
    }

    /// <summary>转回不可变配置。</summary>
    public AiProviderDefinition ToDefinition() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        Endpoint = Endpoint,
        ApiKey = ApiKey,
        Enabled = Enabled,
        RequiresApiKey = RequiresApiKey,
        Models = ParseModels(),
        DefaultModelId = ResolveDefaultModelId(),
        Notes = Notes,
    };
}




using AI.Extension.Providers;
using Microsoft.AspNetCore.Components;

namespace AI.Extension.Blazor;

/// <summary>
/// AI 供应商/模型切换组件的 code-behind。
/// <para>
/// 设计要点：
/// 1) 配置的增删改查仍由 WPF「模型与供应商」页负责，本组件只负责「列出可切换的供应商/模型并切换当前模型」，
///    直接复用 Core 的 <see cref="IAiProviderManager"/>，Blazor 与 WPF 共享同一份
///    <c>%APPDATA%\Karl.AiExtension\providers.json</c>；
/// 2) 只有「已启用且至少有一个模型」的供应商才会成为候选，与管理器内部的
///    <c>EnsureActiveConsistentLocked()</c> 口径保持一致；
/// 3) <see cref="IAiProviderManager.SetActiveAsync"/> 在供应商未启用、模型不存在等情况下会抛异常，
///    因此切换必须 try/catch 并把错误文本展示在组件里，而不是让异常冒泡到 circuit；
/// 4) <see cref="IAiProviderManager.Changed"/> 可能由非渲染线程触发（初始化/切换内部大量
///    ConfigureAwait(false)），回调统一用 <see cref="ComponentBase.InvokeAsync(Action)"/> 封送回渲染上下文；
/// 5) Blazor 默认预渲染：组件在预渲染实例与交互实例中各 <c>OnInitialized</c> 一次，
///    订阅必须在 <see cref="Dispose"/> 里成对取消，否则单例管理器会持有已销毁的组件。
/// </para>
/// </summary>
public partial class AiModelSelector
{
    private IReadOnlyList<AiProviderDefinition> _candidates = Array.Empty<AiProviderDefinition>();
    private IReadOnlyList<AiModelDefinition> _models = Array.Empty<AiModelDefinition>();

    private string? _selectedProviderId;
    private string? _selectedModelId;
    private string? _statusMessage;
    private bool _isError;
    private bool _busy;
    private bool _disposed;

    /// <summary>供应商/模型管理器（由 AddDemoCore / AddAiProviderManagement 注册的单例）。</summary>
    [Inject]
    public IAiProviderManager Manager { get; set; } = default!;

    private bool IsApplyDisabled
        => _busy || string.IsNullOrEmpty(_selectedProviderId) || string.IsNullOrEmpty(_selectedModelId);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // Changed 可能来自后台线程，回调内部统一 InvokeAsync；预渲染实例同样订阅，Dispose 时取消。
        Manager.Changed += OnManagerChanged;
        RefreshSelection();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Manager is not null)
        {
            Manager.Changed -= OnManagerChanged;
        }
    }

    private void OnManagerChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 关键：事件可能来自非渲染线程，直接刷新会破坏 Blazor 的渲染队列。
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            RefreshSelection();
            StateHasChanged();
        });
    }

    /// <summary>重新计算候选列表，并尽量保留当前选择（否则回退到激活供应商 / 第一个候选）。</summary>
    private void RefreshSelection()
    {
        _candidates = Manager.Providers
            .Where(static provider => provider.Enabled && provider.Models.Count > 0)
            .ToArray();

        if (_candidates.Count == 0)
        {
            _selectedProviderId = null;
            _selectedModelId = null;
            _models = Array.Empty<AiModelDefinition>();
            return;
        }

        AiProviderDefinition? active = Manager.ActiveProvider;
        if (!_candidates.Any(candidate => string.Equals(candidate.Id, _selectedProviderId, StringComparison.Ordinal)))
        {
            _selectedProviderId = active is not null &&
                _candidates.Any(candidate => string.Equals(candidate.Id, active.Id, StringComparison.Ordinal))
                ? active.Id
                : _candidates[0].Id;
        }

        SyncModels();
    }

    /// <summary>按当前选中的供应商刷新模型列表，并选出一个合法的模型 Id。</summary>
    private void SyncModels()
    {
        AiProviderDefinition? provider = _candidates
            .FirstOrDefault(candidate => string.Equals(candidate.Id, _selectedProviderId, StringComparison.Ordinal))
            ?? _candidates.FirstOrDefault();

        if (provider is null)
        {
            _selectedProviderId = null;
            _selectedModelId = null;
            _models = Array.Empty<AiModelDefinition>();
            return;
        }

        _selectedProviderId = provider.Id;
        _models = provider.Models;

        if (_models.Any(model => string.Equals(model.Id, _selectedModelId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        bool isActive = string.Equals(Manager.ActiveProvider?.Id, provider.Id, StringComparison.Ordinal);
        string? preferred = isActive ? Manager.ActiveModel?.Id : null;
        preferred ??= provider.DefaultModelId;

        AiModelDefinition? selected = _models
            .FirstOrDefault(model => string.Equals(model.Id, preferred, StringComparison.OrdinalIgnoreCase))
            ?? _models.FirstOrDefault();

        _selectedModelId = selected?.Id;
    }

    private void OnProviderChanged()
    {
        _statusMessage = null;
        _isError = false;
        SyncModels();
    }

    private void OnModelChanged()
    {
        _statusMessage = null;
        _isError = false;
    }

    private async Task ApplyAsync()
    {
        if (IsApplyDisabled)
        {
            return;
        }

        _busy = true;
        _isError = false;

        try
        {
            // 选中供应商默认模型（或用户选择的模型）；未启用/无可用模型时这里会抛异常。
            await Manager.SetActiveAsync(_selectedProviderId, _selectedModelId);
            _statusMessage = $"当前模型已切换为：{Manager.ActiveModelLabel}";
            RefreshSelection();
        }
        catch (Exception exception)
        {
            _statusMessage = "切换失败：" + exception.Message;
            _isError = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ClearAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _isError = false;

        try
        {
            await Manager.SetActiveAsync(null, null);
            _statusMessage = "已清除当前模型，AI 将使用离线兜底客户端。";
            RefreshSelection();
        }
        catch (Exception exception)
        {
            _statusMessage = "清除失败：" + exception.Message;
            _isError = true;
        }
        finally
        {
            _busy = false;
        }
    }
}

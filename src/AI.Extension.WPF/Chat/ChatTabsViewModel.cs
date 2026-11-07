using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using AI.Extension.Chat;
using AI.Extension.Mvvm;
using AI.Extension.WPF.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Extension.WPF.Chat;

/// <summary>「打开会话」下拉里的一项。</summary>
public sealed class ConversationChoice
{
    /// <summary>构造选项。</summary>
    public ConversationChoice(Conversation conversation)
    {
        Conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
    }

    /// <summary>原始会话。</summary>
    public Conversation Conversation { get; }

    /// <summary>显示标题。</summary>
    public string DisplayTitle => Conversation.Title;

    /// <summary>最后更新时间显示。</summary>
    public string UpdatedText => Conversation.UpdatedAt.ToString("MM-dd HH:mm");

    /// <inheritdoc />
    public override string ToString() => DisplayTitle;
}

/// <summary>
/// 多会话标签视图模型：启动时加载未归档会话，新建/关闭/重新打开/归档/删除会话。
/// 每个标签是一个独立的 <see cref="ChatPanelViewModel"/>（对应独立的 <see cref="ChatSession"/>），
/// 因此可以同时和多个会话对话，子智能体也可以各自持有一个会话。
/// </summary>
public sealed class ChatTabsViewModel : ObservableObject
{
    private readonly IChatSessionFactory _sessionFactory;
    private readonly IServiceProvider _services;
    private readonly IAiViewFactory? _viewFactory;
    private ChatPanelViewModel? _selectedTab;
    private ConversationChoice? _selectedConversation;
    private string _statusText = "正在加载会话…";
    private bool _initialized;
    private bool _refreshingChoices;
    private bool _refreshRequested;

    /// <summary>构造标签视图模型。</summary>
    public ChatTabsViewModel(
        IChatSessionFactory sessionFactory,
        IServiceProvider services,
        IAiViewFactory? viewFactory = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _viewFactory = viewFactory;

        NewConversationCommand = new RelayCommand(_ => NewConversation());
        CloseTabCommand = new RelayCommand(CloseTab);
        ArchiveSelectedCommand = new AsyncRelayCommand(_ => ArchiveSelectedAsync(), _ => SelectedTab is not null);
        DeleteSelectedCommand = new AsyncRelayCommand(_ => DeleteSelectedAsync(), _ => SelectedTab is not null);
    }

    /// <summary>打开着的会话标签。</summary>
    public ObservableCollection<ChatPanelViewModel> Tabs { get; } = new();

    /// <summary>可重新打开的会话（已关闭的未归档会话 + 已归档会话）。</summary>
    public ObservableCollection<ConversationChoice> OpenableConversations { get; } = new();

    /// <summary>当前选中的会话。</summary>
    public ChatPanelViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (SetProperty(ref _selectedTab, value))
            {
                ArchiveSelectedCommand.RaiseCanExecuteChanged();
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>「打开会话」下拉选中项；选中后自动打开对应会话。</summary>
    public ConversationChoice? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (!SetProperty(ref _selectedConversation, value) || value is null)
            {
                return;
            }

            _ = OpenConversationAsync(value);
        }
    }

    /// <summary>视图工厂（用于把会话区域替换为自定义视图；为空时用内置视图）。</summary>
    public IAiViewFactory? ViewFactory => _viewFactory;

    /// <summary>状态栏文案（标签数量 / 可打开数量）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>新建会话。</summary>
    public RelayCommand NewConversationCommand { get; }

    /// <summary>关闭会话标签（记录保留，可从「打开会话」重新打开）。</summary>
    public RelayCommand CloseTabCommand { get; }

    /// <summary>归档当前会话。</summary>
    public AsyncRelayCommand ArchiveSelectedCommand { get; }

    /// <summary>删除当前会话。</summary>
    public AsyncRelayCommand DeleteSelectedCommand { get; }

    /// <summary>加载已有未归档会话；没有会话时自动新建一个。可重复调用（只生效一次）。</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            IReadOnlyList<Conversation> conversations = await _sessionFactory.LoadActiveAsync().ConfigureAwait(true);
            foreach (Conversation conversation in conversations.OrderBy(static c => c.UpdatedAt))
            {
                ChatSession? session = await _sessionFactory.OpenAsync(conversation.Id).ConfigureAwait(true);
                if (session is not null)
                {
                    AddTab(session);
                }
            }
        }
        catch (Exception exception)
        {
            StatusText = "加载会话失败：" + exception.Message;
        }

        if (Tabs.Count == 0)
        {
            AddTab(_sessionFactory.CreateNew());
        }

        SelectedTab = Tabs[^1];
        await RefreshOpenableAsync().ConfigureAwait(true);
        UpdateStatus();
    }

    private void NewConversation()
    {
        AddTab(_sessionFactory.CreateNew());
        SelectedTab = Tabs[^1];
        UpdateStatus();
    }

    private void CloseTab(object? parameter)
    {
        if (parameter is not ChatPanelViewModel tab)
        {
            return;
        }

        _ = CloseTabAsync(tab);
    }

    private async Task CloseTabAsync(ChatPanelViewModel tab)
    {
        RemoveTab(tab);
        await RefreshOpenableAsync().ConfigureAwait(true);
        UpdateStatus();
    }

    private async Task OpenConversationAsync(ConversationChoice choice)
    {
        try
        {
            ChatPanelViewModel? existing = Tabs.FirstOrDefault(
                tab => string.Equals(tab.Session.ConversationId, choice.Conversation.Id, StringComparison.Ordinal));
            if (existing is not null)
            {
                SelectedTab = existing;
            }
            else
            {
                ChatSession? session = await _sessionFactory.OpenAsync(choice.Conversation.Id).ConfigureAwait(true);
                if (session is null)
                {
                    StatusText = "会话不存在或已被删除。";
                    return;
                }

                AddTab(session);
                SelectedTab = Tabs[^1];
            }

            await RefreshOpenableAsync().ConfigureAwait(true);
            UpdateStatus();
        }
        catch (Exception exception)
        {
            StatusText = "打开会话失败：" + exception.Message;
        }
        finally
        {
            _selectedConversation = null;
            OnPropertyChanged(nameof(SelectedConversation));
        }
    }

    private async Task ArchiveSelectedAsync()
    {
        ChatPanelViewModel? tab = SelectedTab;
        if (tab is null)
        {
            return;
        }

        try
        {
            await tab.Session.ArchiveAsync().ConfigureAwait(true);
            RemoveTab(tab);
            await RefreshOpenableAsync().ConfigureAwait(true);
            UpdateStatus();
        }
        catch (Exception exception)
        {
            StatusText = "归档失败：" + exception.Message;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        ChatPanelViewModel? tab = SelectedTab;
        if (tab is null)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"确定删除会话「{tab.Title}」？该操作不可恢复。",
            "删除会话",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await tab.Session.DeleteAsync().ConfigureAwait(true);
            RemoveTab(tab);
            await RefreshOpenableAsync().ConfigureAwait(true);
            UpdateStatus();
        }
        catch (Exception exception)
        {
            StatusText = "删除失败：" + exception.Message;
        }
    }

    private void AddTab(ChatSession session)
    {
        ChatPanelViewModel panel = ActivatorUtilities.CreateInstance<ChatPanelViewModel>(_services, session);
        panel.ViewFactory = _viewFactory;
        panel.PropertyChanged += OnPanelPropertyChanged;
        Tabs.Add(panel);
    }

    private void RemoveTab(ChatPanelViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        tab.PropertyChanged -= OnPanelPropertyChanged;
        tab.Dispose();
        Tabs.RemoveAt(index);

        if (Tabs.Count == 0)
        {
            AddTab(_sessionFactory.CreateNew());
        }

        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatPanelViewModel.Title))
        {
            // 标题会随首条消息变化，刷新下拉里的显示
            _ = RefreshOpenableAsync();
        }
    }

    private async Task RefreshOpenableAsync()
    {
        if (_refreshingChoices)
        {
            // 已有刷新在跑：标记需要再来一轮，避免并发刷新把关闭/归档后的状态吞掉
            _refreshRequested = true;
            return;
        }

        _refreshingChoices = true;
        try
        {
            do
            {
                _refreshRequested = false;

                IReadOnlyList<Conversation> active = await _sessionFactory.LoadActiveAsync().ConfigureAwait(true);

                var openIds = Tabs
                    .Select(static tab => tab.Session.ConversationId)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);

                List<Conversation> choices = active
                    .Where(conversation => !openIds.Contains(conversation.Id))
                    .OrderByDescending(static conversation => conversation.UpdatedAt)
                    .ToList();

                OpenableConversations.Clear();
                foreach (Conversation conversation in choices)
                {
                    OpenableConversations.Add(new ConversationChoice(conversation));
                }
            }
            while (_refreshRequested);
        }
        catch (Exception exception)
        {
            StatusText = "读取会话列表失败：" + exception.Message;
        }
        finally
        {
            _refreshingChoices = false;
        }
    }

    private void UpdateStatus()
    {
        StatusText = $"{Tabs.Count} 个标签 · 可打开 {OpenableConversations.Count} 个（自动保存）";
    }
}




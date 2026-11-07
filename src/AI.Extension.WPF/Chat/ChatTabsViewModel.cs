using System.Collections.ObjectModel;
using System.Windows;
using AI.Extension.Chat;
using AI.Extension.Mvvm;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Extension.WPF.Chat;

/// <summary>
/// 多会话标签视图模型：启动时加载未归档会话，新建/关闭/归档/删除会话。
/// 每个标签是一个独立的 <see cref="ChatPanelViewModel"/>（对应独立的 <see cref="ChatSession"/>），
/// 因此可以同时和多个会话对话，子智能体也可以各自持有一个会话。
/// </summary>
public sealed class ChatTabsViewModel : ObservableObject
{
    private readonly IChatSessionFactory _sessionFactory;
    private readonly IServiceProvider _services;
    private ChatPanelViewModel? _selectedTab;
    private string _statusText = "正在加载会话…";
    private bool _initialized;

    /// <summary>构造标签视图模型。</summary>
    public ChatTabsViewModel(IChatSessionFactory sessionFactory, IServiceProvider services)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _services = services ?? throw new ArgumentNullException(nameof(services));

        NewConversationCommand = new RelayCommand(_ => NewConversation());
        CloseTabCommand = new RelayCommand(CloseTab);
        ArchiveSelectedCommand = new AsyncRelayCommand(_ => ArchiveSelectedAsync(), _ => SelectedTab is not null);
        DeleteSelectedCommand = new AsyncRelayCommand(_ => DeleteSelectedAsync(), _ => SelectedTab is not null);
    }

    /// <summary>打开着的会话标签。</summary>
    public ObservableCollection<ChatPanelViewModel> Tabs { get; } = new();

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

    /// <summary>状态栏文案（会话数量 / 存储目录等）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>新建会话。</summary>
    public RelayCommand NewConversationCommand { get; }

    /// <summary>关闭会话标签（保留记录）。</summary>
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
        StatusText = Tabs.Count == 1
            ? "共 1 个会话（自动保存）"
            : $"共 {Tabs.Count} 个会话（自动保存）";
    }

    private void NewConversation()
    {
        AddTab(_sessionFactory.CreateNew());
        SelectedTab = Tabs[^1];
        StatusText = $"共 {Tabs.Count} 个会话（自动保存）";
    }

    private void CloseTab(object? parameter)
    {
        if (parameter is not ChatPanelViewModel tab)
        {
            return;
        }

        RemoveTab(tab);
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
        }
        catch (Exception exception)
        {
            StatusText = "删除失败：" + exception.Message;
        }
    }

    private void AddTab(ChatSession session)
    {
        ChatPanelViewModel panel = ActivatorUtilities.CreateInstance<ChatPanelViewModel>(_services, session);
        Tabs.Add(panel);
    }

    private void RemoveTab(ChatPanelViewModel tab)
    {
        int index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        tab.Dispose();
        Tabs.RemoveAt(index);

        if (Tabs.Count == 0)
        {
            AddTab(_sessionFactory.CreateNew());
        }

        SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        StatusText = Tabs.Count == 1
            ? "共 1 个会话（自动保存）"
            : $"共 {Tabs.Count} 个会话（自动保存）";
    }
}

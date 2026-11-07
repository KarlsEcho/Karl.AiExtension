using AI.Extension.Mcp;

namespace AI.Extension.Chat;

/// <summary>
/// 会话工厂：为多标签 / 子智能体创建互相独立的 <see cref="ChatSession"/>。
/// <para>
/// 每个 <see cref="ChatSession"/> 绑定一个 <see cref="Conversation"/>：
/// 打开时从 <see cref="IConversationStore"/> 恢复历史，每轮结束自动持久化。
/// </para>
/// </summary>
public interface IChatSessionFactory
{
    /// <summary>创建一个全新的空会话（尚未落盘，首条消息后保存）。</summary>
    ChatSession CreateNew();

    /// <summary>按 Id 打开已存在的会话；找不到时返回 null。</summary>
    Task<ChatSession?> OpenAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>加载未归档会话（按更新时间倒序）。</summary>
    Task<IReadOnlyList<Conversation>> LoadActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>加载已归档会话（按更新时间倒序）。</summary>
    Task<IReadOnlyList<Conversation>> LoadArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>归档/取消归档会话。</summary>
    Task SetArchivedAsync(string conversationId, bool archived, CancellationToken cancellationToken = default);

    /// <summary>删除会话。</summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>默认实现：组合当前供应商访问器、MCP 桥、UI 上下文与持久化存储。</summary>
public sealed class ChatSessionFactory : IChatSessionFactory
{
    private readonly IAiChatClientAccessor _chatClientAccessor;
    private readonly McpServerBridge _bridge;
    private readonly IUiActivityTracker _activityTracker;
    private readonly AiChatOptions _options;
    private readonly IConversationStore _store;

    /// <summary>构造工厂。</summary>
    public ChatSessionFactory(
        IAiChatClientAccessor chatClientAccessor,
        McpServerBridge bridge,
        IUiActivityTracker activityTracker,
        AiChatOptions options,
        IConversationStore store)
    {
        _chatClientAccessor = chatClientAccessor ?? throw new ArgumentNullException(nameof(chatClientAccessor));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public ChatSession CreateNew() => new(_chatClientAccessor, _bridge, _activityTracker, _options, new Conversation(), _store);

    /// <inheritdoc />
    public async Task<ChatSession?> OpenAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        Conversation? conversation = await _store.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return conversation is null
            ? null
            : new ChatSession(_chatClientAccessor, _bridge, _activityTracker, _options, conversation, _store);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> LoadActiveAsync(CancellationToken cancellationToken = default)
        => _store.LoadAsync(archived: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> LoadArchivedAsync(CancellationToken cancellationToken = default)
        => _store.LoadAsync(archived: true, cancellationToken);

    /// <inheritdoc />
    public Task SetArchivedAsync(string conversationId, bool archived, CancellationToken cancellationToken = default)
        => _store.SetArchivedAsync(conversationId, archived, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
        => _store.DeleteAsync(conversationId, cancellationToken);
}


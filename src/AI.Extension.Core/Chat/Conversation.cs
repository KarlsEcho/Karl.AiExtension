using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AI.Extension.Chat;

/// <summary>会话里的一条消息（持久化格式：角色 + 文本 + 时间 + 工具轨迹）。</summary>
/// <param name="Role"><c>user</c> / <c>assistant</c>。</param>
/// <param name="Text">消息文本。</param>
/// <param name="Timestamp">消息时间。</param>
/// <param name="ToolCalls">该轮的工具调用轨迹（仅展示用，不进入模型上下文）。</param>
public sealed record ConversationMessage(
    string Role,
    string Text,
    DateTimeOffset Timestamp,
    IReadOnlyList<ConversationToolCall>? ToolCalls = null);

/// <summary>持久化的工具调用轨迹。</summary>
public sealed record ConversationToolCall(
    string ToolName,
    string ArgumentsJson,
    bool Success,
    string ResultPreview,
    long DurationMilliseconds);

/// <summary>一个对话会话（多标签 / 子智能体共用同一模型）。</summary>
public sealed class Conversation
{
    /// <summary>稳定 Id（GUID，无分隔符）。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>会话标题；默认「新对话」，首条用户消息后自动生成。</summary>
    public string Title { get; set; } = "新对话";

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间（用于排序）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>是否已归档（归档后写入 store.json，不参与活动列表）。</summary>
    public bool Archived { get; set; }

    /// <summary>消息列表。</summary>
    public List<ConversationMessage> Messages { get; set; } = new();
}

/// <summary>会话持久化抽象：未归档会话与已归档会话分文件存放。</summary>
public interface IConversationStore
{
    /// <summary>会话文件所在目录（可用于界面展示/排查）。</summary>
    string DirectoryPath { get; }

    /// <summary>按归档状态加载会话（按 <see cref="Conversation.UpdatedAt"/> 倒序）。</summary>
    Task<IReadOnlyList<Conversation>> LoadAsync(bool archived, CancellationToken cancellationToken = default);

    /// <summary>按 Id 查找会话（未归档或已归档）。</summary>
    Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>新增或更新会话（根据 <see cref="Conversation.Archived"/> 决定写入哪个文件）。</summary>
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>删除会话（两个文件里都移除）。</summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>归档/取消归档（在 using.json 与 store.json 之间移动）。</summary>
    Task SetArchivedAsync(string conversationId, bool archived, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON 文件会话存储。
/// <para>
/// 默认目录为 <c>{AppContext.BaseDirectory}/chat/history</c>；
/// 其中 <c>using.json</c> 存放未归档会话、<c>store.json</c> 存放已归档会话。
/// 目录可在 DI 配置（<c>AiExtensionOptions.ConversationStorePath</c>）或直接构造时替换。
/// </para>
/// </summary>
public sealed class JsonConversationStore : IConversationStore
{
    /// <summary>未归档会话文件名。</summary>
    public const string UsingFileName = "using.json";

    /// <summary>已归档会话文件名。</summary>
    public const string ArchivedFileName = "store.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>构造存储；<paramref name="directoryPath"/> 为空时使用默认目录。</summary>
    public JsonConversationStore(string? directoryPath = null)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? Path.Combine(AppContext.BaseDirectory, "chat", "history")
            : directoryPath;
    }

    /// <inheritdoc />
    public string DirectoryPath { get; }

    private string UsingFilePath => Path.Combine(DirectoryPath, UsingFileName);

    private string ArchivedFilePath => Path.Combine(DirectoryPath, ArchivedFileName);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> LoadAsync(bool archived, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await ReadFileAsync(archived ? ArchivedFilePath : UsingFilePath, cancellationToken).ConfigureAwait(false))
                .OrderByDescending(static conversation => conversation.UpdatedAt)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Conversation> usingList = await ReadFileAsync(UsingFilePath, cancellationToken).ConfigureAwait(false);
            Conversation? found = usingList.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            if (found is not null)
            {
                return found;
            }

            List<Conversation> archivedList = await ReadFileAsync(ArchivedFilePath, cancellationToken).ConfigureAwait(false);
            return archivedList.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Conversation> usingList = await ReadFileAsync(UsingFilePath, cancellationToken).ConfigureAwait(false);
            List<Conversation> archivedList = await ReadFileAsync(ArchivedFilePath, cancellationToken).ConfigureAwait(false);

            usingList.RemoveAll(c => string.Equals(c.Id, conversation.Id, StringComparison.Ordinal));
            archivedList.RemoveAll(c => string.Equals(c.Id, conversation.Id, StringComparison.Ordinal));

            if (conversation.Archived)
            {
                archivedList.Add(conversation);
            }
            else
            {
                usingList.Add(conversation);
            }

            await WriteFileAsync(UsingFilePath, usingList, cancellationToken).ConfigureAwait(false);
            await WriteFileAsync(ArchivedFilePath, archivedList, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Conversation> usingList = await ReadFileAsync(UsingFilePath, cancellationToken).ConfigureAwait(false);
            List<Conversation> archivedList = await ReadFileAsync(ArchivedFilePath, cancellationToken).ConfigureAwait(false);

            int removed = usingList.RemoveAll(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            removed += archivedList.RemoveAll(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            if (removed == 0)
            {
                return;
            }

            await WriteFileAsync(UsingFilePath, usingList, cancellationToken).ConfigureAwait(false);
            await WriteFileAsync(ArchivedFilePath, archivedList, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetArchivedAsync(string conversationId, bool archived, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Conversation> usingList = await ReadFileAsync(UsingFilePath, cancellationToken).ConfigureAwait(false);
            List<Conversation> archivedList = await ReadFileAsync(ArchivedFilePath, cancellationToken).ConfigureAwait(false);

            Conversation? conversation = usingList.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal))
                ?? archivedList.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            if (conversation is null)
            {
                return;
            }

            usingList.RemoveAll(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            archivedList.RemoveAll(c => string.Equals(c.Id, conversationId, StringComparison.Ordinal));
            conversation.Archived = archived;
            conversation.UpdatedAt = DateTimeOffset.Now;
            (archived ? archivedList : usingList).Add(conversation);

            await WriteFileAsync(UsingFilePath, usingList, cancellationToken).ConfigureAwait(false);
            await WriteFileAsync(ArchivedFilePath, archivedList, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<List<Conversation>> ReadFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new List<Conversation>();
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<Conversation>();
            }

            return JsonSerializer.Deserialize<List<Conversation>>(json, SerializerOptions) ?? new List<Conversation>();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 会话文件损坏/不可读时降级为空列表，不阻塞界面
            return new List<Conversation>();
        }
    }

    private static async Task WriteFileAsync(string filePath, List<Conversation> conversations, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(conversations, SerializerOptions);
        string temporaryPath = filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(temporaryPath);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败忽略
        }
    }
}

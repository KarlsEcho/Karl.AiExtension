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

    /// <summary>会话标题；默认「新对话」，首轮结束后由 AI 生成，可编辑。</summary>
    public string Title { get; set; } = "新对话";

    /// <summary>创建时间。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间（用于排序）。</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>是否已归档（归档后写入 stored 文件夹，不参与活动列表）。</summary>
    public bool Archived { get; set; }

    /// <summary>消息列表。</summary>
    public List<ConversationMessage> Messages { get; set; } = new();
}

/// <summary>会话持久化抽象：未归档与已归档分文件夹存放，一个会话一个 JSON 文件。</summary>
public interface IConversationStore
{
    /// <summary>会话根目录（其下为 using/ 与 stored/ 两个文件夹）。</summary>
    string DirectoryPath { get; }

    /// <summary>按归档状态加载会话（按 <see cref="Conversation.UpdatedAt"/> 倒序）。</summary>
    Task<IReadOnlyList<Conversation>> LoadAsync(bool archived, CancellationToken cancellationToken = default);

    /// <summary>按 Id 查找会话（未归档或已归档）。</summary>
    Task<Conversation?> GetAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>新增或更新会话（根据 <see cref="Conversation.Archived"/> 写入 using/ 或 stored/）。</summary>
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>删除会话（两个文件夹里都移除）。</summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);

    /// <summary>归档/取消归档（在 using/ 与 stored/ 之间移动文件）。</summary>
    Task SetArchivedAsync(string conversationId, bool archived, CancellationToken cancellationToken = default);
}

/// <summary>
/// 按会话拆分的 JSON 文件存储。
/// <para>
/// 默认根目录为 <c>{AppContext.BaseDirectory}/chat/history</c>，结构：
/// <code>
/// chat/history/
///   using/   {conversationId}.json   // 未归档（界面标签页 / 可筛选）
///   stored/  {conversationId}.json   // 已归档
/// </code>
/// 一个会话一个文件，便于单独查看、备份、筛选。
/// 旧版汇总文件（<c>using.json</c> / <c>store.json</c>）会在首次访问时自动迁移，
/// 迁移完成后原文件保留为 <c>*.legacy</c> 备份。
/// </para>
/// </summary>
public sealed class JsonConversationStore : IConversationStore
{
    /// <summary>未归档会话文件夹名。</summary>
    public const string UsingFolderName = "using";

    /// <summary>已归档会话文件夹名。</summary>
    public const string StoredFolderName = "stored";

    /// <summary>旧版未归档汇总文件名（仅用于自动迁移）。</summary>
    public const string LegacyUsingFileName = "using.json";

    /// <summary>旧版已归档汇总文件名（仅用于自动迁移）。</summary>
    public const string LegacyStoredFileName = "store.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _legacyMigrated;

    /// <summary>构造存储；<paramref name="directoryPath"/> 为空时使用默认根目录。</summary>
    public JsonConversationStore(string? directoryPath = null)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directoryPath)
            ? Path.Combine(AppContext.BaseDirectory, "chat", "history")
            : directoryPath;
    }

    /// <inheritdoc />
    public string DirectoryPath { get; }

    private string UsingDirectory => Path.Combine(DirectoryPath, UsingFolderName);

    private string StoredDirectory => Path.Combine(DirectoryPath, StoredFolderName);

    private string GetDirectory(bool archived) => archived ? StoredDirectory : UsingDirectory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> LoadAsync(bool archived, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLegacyMigratedAsync(cancellationToken).ConfigureAwait(false);

            string directory = GetDirectory(archived);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<Conversation>();
            }

            var conversations = new List<Conversation>();
            foreach (string filePath in Directory.EnumerateFiles(directory, "*.json"))
            {
                Conversation? conversation = await ReadConversationAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (conversation is not null)
                {
                    conversation.Archived = archived;
                    conversations.Add(conversation);
                }
            }

            return conversations
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
            await EnsureLegacyMigratedAsync(cancellationToken).ConfigureAwait(false);
            return await ReadByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
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
            await EnsureLegacyMigratedAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(conversation, cancellationToken).ConfigureAwait(false);
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
            await EnsureLegacyMigratedAsync(cancellationToken).ConfigureAwait(false);
            TryDeleteFile(GetFilePath(conversationId, archived: false));
            TryDeleteFile(GetFilePath(conversationId, archived: true));
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
            await EnsureLegacyMigratedAsync(cancellationToken).ConfigureAwait(false);

            Conversation? conversation = await ReadByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
            if (conversation is null)
            {
                return;
            }

            conversation.Archived = archived;
            conversation.UpdatedAt = DateTimeOffset.Now;
            await WriteAsync(conversation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Conversation?> ReadByIdAsync(string conversationId, CancellationToken cancellationToken)
    {
        foreach (bool archived in new[] { false, true })
        {
            string filePath = GetFilePath(conversationId, archived);
            if (!File.Exists(filePath))
            {
                continue;
            }

            Conversation? conversation = await ReadConversationAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (conversation is not null)
            {
                conversation.Archived = archived;
                return conversation;
            }
        }

        return null;
    }

    private async Task WriteAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        string targetPath = GetFilePath(conversation.Id, conversation.Archived);
        string otherPath = GetFilePath(conversation.Id, !conversation.Archived);

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await WriteJsonAsync(targetPath, conversation, cancellationToken).ConfigureAwait(false);
        TryDeleteFile(otherPath);
    }

    private static async Task WriteJsonAsync(string filePath, Conversation conversation, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(conversation, SerializerOptions);
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

    private static async Task<Conversation?> ReadConversationAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<Conversation>(json, SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 单个会话文件损坏时跳过，不影响其他会话
            return null;
        }
    }

    /// <summary>旧版汇总文件自动迁移为「一个会话一个文件」。</summary>
    private async Task EnsureLegacyMigratedAsync(CancellationToken cancellationToken)
    {
        if (_legacyMigrated)
        {
            return;
        }

        _legacyMigrated = true;
        await MigrateLegacyFileAsync(
            Path.Combine(DirectoryPath, LegacyUsingFileName),
            UsingDirectory,
            archived: false,
            cancellationToken).ConfigureAwait(false);
        await MigrateLegacyFileAsync(
            Path.Combine(DirectoryPath, LegacyStoredFileName),
            StoredDirectory,
            archived: true,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task MigrateLegacyFileAsync(
        string legacyFilePath,
        string targetDirectory,
        bool archived,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(legacyFilePath))
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(legacyFilePath, cancellationToken).ConfigureAwait(false);
            List<Conversation>? conversations = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<List<Conversation>>(json, SerializerOptions);

            if (conversations is not null && conversations.Count > 0)
            {
                Directory.CreateDirectory(targetDirectory);
                foreach (Conversation conversation in conversations)
                {
                    conversation.Archived = archived;
                    string targetPath = Path.Combine(targetDirectory, SafeFileId(conversation.Id) + ".json");
                    await WriteJsonAsync(targetPath, conversation, cancellationToken).ConfigureAwait(false);
                }
            }

            // 迁移完成保留备份，避免下次重复迁移
            File.Move(legacyFilePath, legacyFilePath + ".legacy", overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 迁移失败不阻塞使用：旧文件保留，下次启动会重试
        }
    }

    private string GetFilePath(string conversationId, bool archived)
        => Path.Combine(GetDirectory(archived), SafeFileId(conversationId) + ".json");

    private static string SafeFileId(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return Guid.NewGuid().ToString("N");
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(conversationId.Length);
        foreach (char ch in conversationId)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 || ch is '.' or '\\' or '/' ? '_' : ch);
        }

        return builder.Length == 0 ? Guid.NewGuid().ToString("N") : builder.ToString();
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // 清理失败忽略
        }
    }
}

using System.IO;
using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using Microsoft.Extensions.AI;

namespace Demo.Tests;

/// <summary>会话存储 / 会话工厂测试（多标签与持久化的基础）。</summary>
public class ConversationStoreTests
{
    [Fact]
    public async Task 未归档会话_保存后可加载()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var conversation = new Conversation { Title = "测试会话" };
            conversation.Messages.Add(new ConversationMessage("user", "你好", DateTimeOffset.Now));

            await store.SaveAsync(conversation);

            IReadOnlyList<Conversation> active = await store.LoadAsync(archived: false);
            Assert.Single(active);
            Assert.Equal(conversation.Id, active[0].Id);
            Assert.Equal("测试会话", active[0].Title);
            Assert.Equal("你好", active[0].Messages[0].Text);
            string? savedFile = FindConversationFile(directory, JsonConversationStore.UsingFolderName, conversation.Id);
            Assert.NotNull(savedFile);
            Assert.StartsWith("测试会话_", Path.GetFileName(savedFile));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 归档会话_移动到store文件()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var conversation = new Conversation { Title = "待归档" };
            conversation.Messages.Add(new ConversationMessage("user", "hi", DateTimeOffset.Now));
            await store.SaveAsync(conversation);

            await store.SetArchivedAsync(conversation.Id, archived: true);

            Assert.Empty(await store.LoadAsync(archived: false));
            IReadOnlyList<Conversation> archived = await store.LoadAsync(archived: true);
            Assert.Single(archived);
            Assert.True(archived[0].Archived);
            Assert.NotNull(FindConversationFile(directory, JsonConversationStore.StoredFolderName, conversation.Id));
            Assert.Null(FindConversationFile(directory, JsonConversationStore.UsingFolderName, conversation.Id));

            await store.SetArchivedAsync(conversation.Id, archived: false);
            Assert.Single(await store.LoadAsync(archived: false));
            Assert.Empty(await store.LoadAsync(archived: true));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 删除会话_两个文件都移除()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var conversation = new Conversation { Title = "删除我" };
            await store.SaveAsync(conversation);
            await store.SetArchivedAsync(conversation.Id, archived: true);

            await store.DeleteAsync(conversation.Id);

            Assert.Null(await store.GetAsync(conversation.Id));
            Assert.Empty(await store.LoadAsync(archived: false));
            Assert.Empty(await store.LoadAsync(archived: true));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 会话工厂_打开时恢复历史并持久化重命名()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var factory = new ChatSessionFactory(
                new FixedChatClientAccessor(),
                new McpServerBridge(),
                new UiActivityTracker(),
                new AiChatOptions(),
                store);

            ChatSession fresh = factory.CreateNew();
            Assert.NotNull(fresh.ConversationId);
            Assert.Equal("新对话", fresh.ConversationTitle);

            var conversation = new Conversation { Title = "历史会话" };
            conversation.Messages.Add(new ConversationMessage("user", "第一问", DateTimeOffset.Now));
            conversation.Messages.Add(new ConversationMessage("assistant", "第一答", DateTimeOffset.Now));
            await store.SaveAsync(conversation);

            ChatSession? reopened = await factory.OpenAsync(conversation.Id);
            Assert.NotNull(reopened);
            Assert.Equal(2, reopened!.HistoryCount);
            Assert.Equal("历史会话", reopened.ConversationTitle);

            await reopened.RenameAsync("改个名字");
            Conversation? saved = await store.GetAsync(conversation.Id);
            Assert.NotNull(saved);
            Assert.Equal("改个名字", saved!.Title);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 多标签会话_彼此历史隔离()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var factory = new ChatSessionFactory(
                new FixedChatClientAccessor(),
                new McpServerBridge(),
                new UiActivityTracker(),
                new AiChatOptions(),
                store);

            ChatSession first = factory.CreateNew();
            ChatSession second = factory.CreateNew();
            Assert.NotEqual(first.ConversationId, second.ConversationId);
            Assert.Equal(0, first.HistoryCount);
            Assert.Equal(0, second.HistoryCount);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 旧版汇总文件_自动迁移为按会话拆分()
    {
        string directory = NewTempDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            var legacyConversation = new Conversation { Title = "旧会话" };
            legacyConversation.Messages.Add(new ConversationMessage("user", "hi", DateTimeOffset.Now));
            await File.WriteAllTextAsync(
                Path.Combine(directory, JsonConversationStore.LegacyUsingFileName),
                System.Text.Json.JsonSerializer.Serialize(new List<Conversation> { legacyConversation }));

            var store = new JsonConversationStore(directory);
            IReadOnlyList<Conversation> active = await store.LoadAsync(archived: false);

            Assert.Single(active);
            Assert.Equal("旧会话", active[0].Title);
            string? migratedFile = FindConversationFile(directory, JsonConversationStore.UsingFolderName, active[0].Id);
            Assert.NotNull(migratedFile);
            Assert.StartsWith("旧会话_", Path.GetFileName(migratedFile));
            Assert.False(File.Exists(Path.Combine(directory, JsonConversationStore.LegacyUsingFileName)));
            Assert.True(File.Exists(Path.Combine(directory, JsonConversationStore.LegacyUsingFileName + ".legacy")));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 重命名会话_文件名跟随标题变化()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var conversation = new Conversation { Title = "旧标题" };
            await store.SaveAsync(conversation);
            Assert.StartsWith("旧标题_", Path.GetFileName(FindConversationFile(directory, JsonConversationStore.UsingFolderName, conversation.Id)!));

            conversation.Title = "新标题";
            await store.SaveAsync(conversation);

            string? renamedFile = FindConversationFile(directory, JsonConversationStore.UsingFolderName, conversation.Id);
            Assert.NotNull(renamedFile);
            Assert.StartsWith("新标题_", Path.GetFileName(renamedFile));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(directory, JsonConversationStore.UsingFolderName), "*.json"));
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string? FindConversationFile(string directory, string folder, string conversationId)
    {
        string target = Path.Combine(directory, folder);
        if (!Directory.Exists(target))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(target, "*.json")
            .FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Equals(conversationId, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("_" + conversationId, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static string NewTempDirectory()
        => Path.Combine(Path.GetTempPath(), "karl-ai-conversation-tests", Guid.NewGuid().ToString("N"));

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // 测试清理失败忽略
        }
    }

    private sealed class FixedChatClientAccessor : IAiChatClientAccessor
    {
        private readonly IChatClient _client = new UnconfiguredChatClient();

        public IChatClient Current => _client;

        public string DisplayName => "测试模式";

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}


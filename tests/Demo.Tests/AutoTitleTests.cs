using System.IO;
using System.Runtime.CompilerServices;
using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using Microsoft.Extensions.AI;

namespace Demo.Tests;

/// <summary>首轮对话结束后 AI 自动生成标题的测试。</summary>
public class AutoTitleTests
{
    [Fact]
    public async Task 首轮结束后_使用模型生成的标题并持久化()
    {
        string directory = Path.Combine(Path.GetTempPath(), "karl-ai-title-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConversationStore(directory);
            var accessor = new FakeChatClientAccessor(new TitleFakeChatClient());
            var conversation = new Conversation();
            var session = new ChatSession(
                accessor,
                new McpServerBridge(),
                new UiActivityTracker(),
                new AiChatOptions(),
                conversation,
                store);

            string? raisedTitle = null;
            session.TitleChanged += title => raisedTitle = title;

            await session.SendAsync("帮我统计一下订单情况");

            Assert.Equal("订单统计", session.ConversationTitle);
            Assert.Equal("订单统计", raisedTitle);

            Conversation? saved = await store.GetAsync(conversation.Id);
            Assert.NotNull(saved);
            Assert.Equal("订单统计", saved!.Title);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 关闭自动标题时_回退为首条消息截断()
    {
        string directory = Path.Combine(Path.GetTempPath(), "karl-ai-title-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConversationStore(directory);
            var accessor = new FakeChatClientAccessor(new TitleFakeChatClient());
            var conversation = new Conversation();
            var options = new AiChatOptions { AutoTitleFromFirstRound = false };
            var session = new ChatSession(
                accessor,
                new McpServerBridge(),
                new UiActivityTracker(),
                options,
                conversation,
                store);

            await session.SendAsync("帮我统计一下订单情况");

            Assert.Equal("帮我统计一下订单情况", session.ConversationTitle);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 未配置供应商时_标题回退为首条消息()
    {
        string directory = Path.Combine(Path.GetTempPath(), "karl-ai-title-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConversationStore(directory);
            var accessor = new FakeChatClientAccessor(new TitleFakeChatClient(), isConfigured: false);
            var session = new ChatSession(
                accessor,
                new McpServerBridge(),
                new UiActivityTracker(),
                new AiChatOptions(),
                new Conversation(),
                store);

            await session.SendAsync("帮我统计一下订单情况");

            Assert.Equal("帮我统计一下订单情况", session.ConversationTitle);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }
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

    private sealed class FakeChatClientAccessor : IAiChatClientAccessor
    {
        private readonly IChatClient _client;

        public FakeChatClientAccessor(IChatClient client, bool isConfigured = true)
        {
            _client = client;
            IsConfigured = isConfigured;
        }

        public IChatClient Current => _client;

        public string DisplayName => "测试模式";

        public bool IsConfigured { get; }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    /// <summary>第一次调用返回对话回复，第二次调用返回标题。</summary>
    private sealed class TitleFakeChatClient : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _calls++;
            string text = _calls == 1 ? "这是第一轮回答" : "订单统计";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}


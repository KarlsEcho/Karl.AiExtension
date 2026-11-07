using System.IO;
using System.Runtime.CompilerServices;
using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using Microsoft.Extensions.AI;

namespace Demo.Tests;

/// <summary>流式返回测试：增量顺序、工具轮次、非流式回退。</summary>
public class StreamingChatTests
{
    [Fact]
    public async Task 流式返回_增量顺序正确且最终落盘()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var delta = new List<string>();
            var session = CreateSession(new TextStreamingClient("你", "好", "，", "世界"), store, delta);

            AiChatReply reply = await session.SendStreamingAsync("打个招呼");

            Assert.Equal("你好，世界", reply.Text);
            Assert.Equal(new[] { "你", "好", "，", "世界" }, delta);
            Assert.Equal(2, session.HistoryCount);

            Conversation? saved = await store.GetAsync(session.ConversationId!);
            Assert.NotNull(saved);
            Assert.Equal(2, saved!.Messages.Count);
            Assert.Equal("你好，世界", saved.Messages[1].Text);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 流式工具调用失败后_继续下一轮并返回文本()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var delta = new List<string>();
            var session = CreateSession(new ToolThenTextStreamingClient(), store, delta);

            AiChatReply reply = await session.SendStreamingAsync("查一下订单");

            Assert.Equal("工具失败后的答复", reply.Text);
            Assert.Single(reply.ToolCalls);
            Assert.False(reply.ToolCalls[0].Success);
            Assert.Contains("工具失败后的答复", delta);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task 不支持流式的客户端_自动回退为一次性返回()
    {
        string directory = NewTempDirectory();
        try
        {
            var store = new JsonConversationStore(directory);
            var delta = new List<string>();
            var session = CreateSession(new NonStreamingOnlyClient(), store, delta);

            AiChatReply reply = await session.SendStreamingAsync("你好");

            Assert.Equal("兜底回答", reply.Text);
            Assert.Empty(delta);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static ChatSession CreateSession(IChatClient client, IConversationStore store, List<string> delta)
    {
        var session = new ChatSession(
            new FixedChatClientAccessor(client),
            new McpServerBridge(),
            new UiActivityTracker(),
            new AiChatOptions { AutoTitleFromFirstRound = false },
            new Conversation(),
            store);

        session.TextDelta += delta.Add;
        return session;
    }

    private static string NewTempDirectory()
        => Path.Combine(Path.GetTempPath(), "karl-ai-streaming-tests", Guid.NewGuid().ToString("N"));

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
        private readonly IChatClient _client;

        public FixedChatClientAccessor(IChatClient client)
        {
            _client = client;
        }

        public IChatClient Current => _client;

        public string DisplayName => "测试模式";

        public bool IsConfigured => true;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class TextStreamingClient : IChatClient
    {
        private readonly string[] _chunks;

        public TextStreamingClient(params string[] chunks)
        {
            _chunks = chunks;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (string chunk in _chunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                await Task.Yield();
            }
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(_chunks))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ToolThenTextStreamingClient : IChatClient
    {
        private int _streamingCalls;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _streamingCalls++;
            if (_streamingCalls == 1)
            {
                var contents = new List<AIContent>
                {
                    new FunctionCallContent("call-1", "no_such_tool"),
                };
                yield return new ChatResponseUpdate(ChatRole.Assistant, contents);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "工具失败后的答复");
            await Task.Yield();
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "工具失败后的答复")));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class NonStreamingOnlyClient : IChatClient
    {
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("该客户端不支持流式返回。");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "兜底回答")));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}


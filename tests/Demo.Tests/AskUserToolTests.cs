using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.WPF.Chat;
using Microsoft.Extensions.AI;

namespace Demo.Tests;

/// <summary>内置 ask_user 工具与选择卡片测试。</summary>
public class AskUserToolTests
{
    [Fact]
    public async Task ask_user工具_会带上定义并等待用户选择()
    {
        var client = new AskUserThenTextClient();
        var session = CreateSession(client);
        AiAskUserRequest? captured = null;

        session.UserInputHandler = (request, _) =>
        {
            captured = request;
            return Task.FromResult(new AiAskUserResult("B", null, Cancelled: false));
        };

        AiChatReply reply = await session.SendAsync("帮我选一个订单");

        Assert.NotNull(captured);
        Assert.Equal("请选择要处理的订单", captured!.Question);
        Assert.Equal(new[] { "A", "B" }, captured.Options);
        Assert.False(captured.AllowFreeText);

        Assert.Contains(client.OfferedToolNames, name => name == "ask_user");
        Assert.Equal("已按 B 处理", reply.Text);
        AiToolCallTrace trace = Assert.Single(reply.ToolCalls);
        Assert.Equal("ask_user", trace.ToolName);
        Assert.True(trace.Success);
        Assert.Contains("用户选择：B", trace.ResultPreview);
        Assert.Contains("用户选择：B", client.SecondCallToolPayload);
    }

    [Fact]
    public async Task 宿主没有交互能力时_返回不支持并继续对话()
    {
        var client = new AskUserThenTextClient();
        var session = CreateSession(client);

        AiChatReply reply = await session.SendAsync("帮我选一个订单");

        Assert.Equal("已按 B 处理", reply.Text);
        AiToolCallTrace trace = Assert.Single(reply.ToolCalls);
        Assert.False(trace.Success);
        Assert.Contains("宿主不支持用户交互", trace.ResultPreview);
    }

    [Fact]
    public void 选择卡片_选项点击后完成并展示结果()
    {
        AiAskUserResult? result = null;
        var message = ChatMessageViewModel.CreateChoice(
            new AiAskUserRequest("请选择订单", new[] { "SO-1001", "SO-1002" }, AllowFreeText: false),
            selected => result = selected);

        Assert.True(message.IsChoiceRequest);
        Assert.Equal("请选择订单", message.ChoiceQuestion);
        Assert.False(message.IsChoiceResolved);

        message.SelectChoiceCommand.Execute("SO-1002");

        Assert.True(message.IsChoiceResolved);
        Assert.Contains("SO-1002", message.ChoiceResultText);
        Assert.NotNull(result);
        Assert.Equal("SO-1002", result!.SelectedOption);
    }

    [Fact]
    public void 选择卡片_支持自由输入()
    {
        AiAskUserResult? result = null;
        var message = ChatMessageViewModel.CreateChoice(
            new AiAskUserRequest("补充订单号", Array.Empty<string>(), AllowFreeText: true),
            selected => result = selected);

        message.ChoiceFreeText = "SO-9999";
        message.SubmitChoiceCommand.Execute(null);

        Assert.True(message.IsChoiceResolved);
        Assert.NotNull(result);
        Assert.Equal("SO-9999", result!.FreeText);
    }

    private static ChatSession CreateSession(IChatClient client)
        => new(
            new FixedChatClientAccessor(client),
            new McpServerBridge(),
            new UiActivityTracker(),
            new AiChatOptions { AutoTitleFromFirstRound = false });

    private sealed class FixedChatClientAccessor : IAiChatClientAccessor
    {
        private readonly IChatClient _client;

        public FixedChatClientAccessor(IChatClient client) => _client = client;

        public IChatClient Current => _client;

        public string DisplayName => "测试模型";

        public bool IsConfigured => true;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class AskUserThenTextClient : IChatClient
    {
        private int _calls;

        public List<string> OfferedToolNames { get; } = new();

        public string SecondCallToolPayload { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _calls++;
            List<ChatMessage> messageList = messages.ToList();

            if (_calls == 1)
            {
                OfferedToolNames.AddRange(options?.Tools?.Select(tool => tool.Name) ?? Array.Empty<string>());

                var call = new FunctionCallContent(
                    "call-1",
                    "ask_user",
                    new Dictionary<string, object?>
                    {
                        ["question"] = "请选择要处理的订单",
                        ["options"] = new[] { "A", "B" },
                        ["allowFreeText"] = false,
                    });

                return Task.FromResult(new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, new List<AIContent> { call })));
            }

            SecondCallToolPayload = string.Concat(
                messageList
                    .Where(message => message.Role == ChatRole.Tool)
                    .SelectMany(message => message.Contents)
                    .OfType<FunctionResultContent>()
                    .Select(content => content.Result?.ToString() ?? string.Empty));

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "已按 B 处理")));
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


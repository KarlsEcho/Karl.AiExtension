using System.IO;
using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using AI.Extension.WPF.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Tests;

/// <summary>多标签会话的交互逻辑测试：关闭后可从「打开会话」列表重新打开。</summary>
public class ChatTabsViewModelTests
{
    [Fact]
    public async Task 关闭会话后_可从可打开列表重新打开()
    {
        string directory = Path.Combine(Path.GetTempPath(), "karl-ai-tabs-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonConversationStore(directory);
            var conversation = new Conversation { Title = "会话A" };
            conversation.Messages.Add(new ConversationMessage("user", "你好", DateTimeOffset.Now));
            conversation.Messages.Add(new ConversationMessage("assistant", "你好，有什么可以帮你？", DateTimeOffset.Now));
            await store.SaveAsync(conversation);

            var accessor = new FixedChatClientAccessor();
            var factory = new ChatSessionFactory(
                accessor,
                new McpServerBridge(),
                new UiActivityTracker(),
                new AiChatOptions(),
                store);

            var services = new ServiceCollection();
            services.AddSingleton<IUiDispatcher>(new ImmediateUiDispatcher());
            services.AddSingleton<IAiChatClientAccessor>(accessor);
            services.AddSingleton<IChatSessionFactory>(factory);
            using ServiceProvider provider = services.BuildServiceProvider();

            var tabs = new ChatTabsViewModel(factory, provider);
            await tabs.InitializeAsync();

            Assert.Single(tabs.Tabs);
            ChatPanelViewModel original = tabs.Tabs[0];
            Assert.Equal("会话A", original.Title);
            Assert.Equal(2, original.Messages.Count);
            Assert.Empty(tabs.OpenableConversations);

            tabs.CloseTabCommand.Execute(original);
            await WaitUntilAsync(() =>
                tabs.Tabs.Count == 1
                && !ReferenceEquals(tabs.Tabs[0], original)
                && tabs.OpenableConversations.Count == 1);
            Assert.Equal("会话A", tabs.OpenableConversations[0].Conversation.Title);

            tabs.SelectedConversation = tabs.OpenableConversations[0];
            await WaitUntilAsync(() => tabs.Tabs.Count == 2 && tabs.OpenableConversations.Count == 0);

            ChatPanelViewModel reopened = tabs.Tabs[1];
            Assert.Equal("会话A", reopened.Title);
            Assert.Equal(2, reopened.Messages.Count);
            Assert.Empty(tabs.OpenableConversations);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 8000)
    {
        DateTimeOffset deadline = DateTimeOffset.Now.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTimeOffset.Now > deadline)
            {
                throw new TimeoutException("等待条件超时。");
            }

            await Task.Delay(20);
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


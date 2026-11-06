using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace AI.Extension.Chat;

/// <summary>按配置创建 <see cref="IChatClient"/>：已配置则走 OpenAI 兼容端点，否则返回未配置提示客户端。</summary>
public static class AiChatClientFactory
{
    /// <summary>创建聊天客户端。</summary>
    public static IChatClient Create(AiChatOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsRemoteConfigured)
        {
            return new UnconfiguredChatClient();
        }

        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            clientOptions.Endpoint = new Uri(options.Endpoint, UriKind.Absolute);
        }

        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(options.ApiKey!), clientOptions);
        return openAiClient.GetChatClient(options.Model!).AsIChatClient();
    }

    /// <summary>未配置模型时使用的客户端：给出可执行的配置指引，保证应用能跑起来。</summary>
    private sealed class UnconfiguredChatClient : IChatClient
    {
        private const string Message = """
            当前没有配置大模型，AI 对话处于离线状态。
            设置环境变量后重启应用即可接通：
            - KARL_AI_ENDPOINT（OpenAI 兼容端点，例如 https://api.deepseek.com/v1）
            - KARL_AI_API_KEY
            - KARL_AI_MODEL（例如 deepseek-chat）
            离线状态下 MCP 工具仍然可用，可在右侧「工具」页签手动调用验证闭环。
            """;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Message)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Message);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

using Microsoft.Extensions.AI;

namespace AI.Extension.Chat;

/// <summary>
/// 未配置任何供应商时使用的兜底客户端：给出可执行的配置指引，保证应用永远能启动。
/// </summary>
public sealed class UnconfiguredChatClient : IChatClient
{
    private const string Message = """
        当前还没有配置 AI 供应商或 API Key，AI 对话处于离线状态。
        请到「模型与供应商」页签：选择内置供应商（DeepSeek / OpenAI / 通义 / Ollama）、填写 API Key，
        保存后点「设为当前模型」即可接通。
        离线状态下 MCP 工具仍然可用，可在「工具与契约」页签查看。
        """;

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Message)));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Message);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

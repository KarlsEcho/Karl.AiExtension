using Microsoft.Extensions.AI;
using OpenAI;

namespace AI.Extension.Providers;

/// <summary>按供应商配置创建 <see cref="IChatClient"/>（当前只支持 OpenAI 兼容协议）。</summary>
public static class AiProviderClientFactory
{
    /// <summary>为指定供应商与模型创建聊天客户端。</summary>
    public static IChatClient Create(AiProviderDefinition provider, AiModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(model);

        if (provider.Kind != AiProviderKind.OpenAICompatible)
        {
            throw new NotSupportedException($"暂不支持供应商类型 {provider.Kind}。");
        }

        if (string.IsNullOrWhiteSpace(provider.Endpoint))
        {
            throw new InvalidOperationException($"供应商「{provider.DisplayName}」没有配置 Endpoint。");
        }

        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new InvalidOperationException($"供应商「{provider.DisplayName}」的模型 Id 为空。");
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(provider.Endpoint, UriKind.Absolute),
        };

        // 本地推理（Ollama 等）通常不校验 Key，但 SDK 要求非空字符串
        string apiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? "not-required" : provider.ApiKey!;
        var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), clientOptions);

        return openAiClient.GetChatClient(model.Id).AsIChatClient();
    }
}

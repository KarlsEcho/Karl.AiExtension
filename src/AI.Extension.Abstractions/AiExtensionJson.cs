using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AI.Extension;

/// <summary>框架内共享的 JSON 配置工厂。</summary>
public static class AiExtensionJson
{
    /// <summary>
    /// 创建 MCP 工具参数/返回值的序列化配置：
    /// Web 默认（camelCase）+ 中文不转义 + 枚举序列化为字符串 + 反射解析器。
    /// </summary>
    public static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

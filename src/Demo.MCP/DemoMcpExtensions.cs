using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Demo.MCP;

/// <summary>Demo.MCP 的注册入口：把本程序集里所有 [McpServerToolType] 注册给 MCP 服务器。</summary>
public static class DemoMcpExtensions
{
    /// <summary>
    /// 工具参数/返回值的序列化配置：
    /// Web 默认（camelCase）便于模型理解，Relaxed 编码避免中文被转成 \uXXXX。
    /// 显式指定 TypeInfoResolver，否则 STJ 在只读化时会抛异常。
    /// </summary>
    private static readonly JsonSerializerOptions ToolSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>注册 Demo 的全部 MCP 工具（基于微软 MCP C# SDK 的程序集扫描）。</summary>
    public static IMcpServerBuilder AddDemoMcpTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithToolsFromAssembly(typeof(DemoMcpExtensions).Assembly, ToolSerializerOptions);
    }
}


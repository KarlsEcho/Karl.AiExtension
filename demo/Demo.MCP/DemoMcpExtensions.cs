using System.Text.Json;
using AI.Extension;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Demo.MCP;

/// <summary>Demo.MCP 的注册入口：把本程序集里所有 [McpServerToolType] 注册给 MCP 服务器。</summary>
public static class DemoMcpExtensions
{
    /// <summary>工具参数/返回值的序列化配置（与框架默认一致：camelCase、中文不转义、枚举转字符串）。</summary>
    private static readonly JsonSerializerOptions ToolSerializerOptions = AiExtensionJson.CreateToolSerializerOptions();

    /// <summary>注册 Demo 的全部 MCP 工具（基于微软 MCP C# SDK 的程序集扫描）。</summary>
    public static IMcpServerBuilder AddDemoMcpTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithToolsFromAssembly(typeof(DemoMcpExtensions).Assembly, ToolSerializerOptions);
    }

    /// <summary>本程序集（宿主一键接入时传给 <see cref="AiExtensionOptions.UseMcpToolsFrom"/>）。</summary>
    public static System.Reflection.Assembly Assembly => typeof(DemoMcpExtensions).Assembly;
}

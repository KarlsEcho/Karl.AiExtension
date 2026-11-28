using System.ComponentModel;
using AI.Extension;
using ModelContextProtocol.Server;

namespace Demo.SecondTools;

/// <summary>第二个插件程序集自带的契约（用于验证多程序集聚合）。</summary>
public interface ISecondService
{
    string Ping(string name);
}

[AiContractBinding(typeof(ISecondService), Name = "SecondService")]
[AiMethod(nameof(ISecondService.Ping), ToolName = "second_ping", Description = "第二个插件的 ping 工具")]
internal sealed class SecondServiceBinding;

/// <summary>第二个插件程序集的 MCP 工具。</summary>
[McpServerToolType]
public sealed class SecondPluginTools(ISecondService service)
{
    [McpServerTool, Description("第二个插件程序集的 echo 工具")]
    public string second_echo([Description("要回显的文本")] string text)
        => "second:" + service.Ping(text);
}


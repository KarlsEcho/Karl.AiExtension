namespace AI.Extension.WPF.Mcp;

/// <summary>对外暴露 MCP 的传输模式。</summary>
[Flags]
public enum AiMcpExposureMode
{
    /// <summary>不对外暴露。</summary>
    None = 0,

    /// <summary>Streamable HTTP（仅 net10 目标支持）。</summary>
    Http = 1,

    /// <summary>stdio（通过 WPF 命名管道 + AiMcpStdioBridge.exe）。</summary>
    Stdio = 2,
}

/// <summary>MCP 对外暴露配置。</summary>
public sealed class AiMcpExposureOptions
{
    /// <summary>要开启的传输模式（可组合）。</summary>
    public AiMcpExposureMode Mode { get; set; } = AiMcpExposureMode.None;

    /// <summary>HTTP 模式端口（仅 127.0.0.1）。</summary>
    public int HttpPort { get; set; } = 5199;

    /// <summary>stdio 模式的命名管道名。</summary>
    public string StdioPipeName { get; set; } = "Karl.AiExtension.Mcp";

    /// <summary>stdio 模式的 Bridge 路径；为空时使用应用目录下的 AiMcpStdioBridge.exe。</summary>
    public string? StdioBridgePath { get; set; }

    /// <summary>宿主启动时是否自动开启配置的暴露模式。</summary>
    public bool AutoStart { get; set; } = true;
}

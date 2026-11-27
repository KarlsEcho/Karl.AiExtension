namespace AI.Extension.WPF.Mcp;

/// <summary>
/// 在**当前进程内**把 MCP Server 通过 Streamable HTTP 暴露到本机端口。
/// <para>net48 目标不支持该传输（依赖 ASP.NET Core），请使用 stdio 管道模式。</para>
/// </summary>
public interface IMcpHttpExposureService : IAsyncDisposable
{
    /// <summary>当前目标框架是否支持 HTTP 暴露（net48 为 false）。</summary>
    bool IsSupported { get; }

    /// <summary>是否已开启暴露。</summary>
    bool IsRunning { get; }

    /// <summary>开启后的访问地址（例如 <c>http://127.0.0.1:5199</c>）；未开启时为 null。</summary>
    string? EndpointUrl { get; }

    /// <summary>在指定端口开启暴露，返回访问地址。</summary>
    Task<string> StartAsync(int port, CancellationToken cancellationToken = default);

    /// <summary>关闭暴露。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

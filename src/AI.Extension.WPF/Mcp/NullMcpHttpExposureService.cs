#if !NET10_0_OR_GREATER
namespace AI.Extension.WPF.Mcp;

/// <summary>net48 目标下 HTTP 暴露不可用，使用该空实现让诊断面板/宿主保持可运行。</summary>
internal sealed class NullMcpHttpExposureService : IMcpHttpExposureService
{
    public bool IsSupported => false;

    public bool IsRunning => false;

    public string? EndpointUrl => null;

    public Task<string> StartAsync(int port, CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException(
            "net48 目标不支持 Streamable HTTP MCP 暴露（依赖 ASP.NET Core）；请使用 stdio 模式。");

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => default;
}
#endif

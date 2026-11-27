namespace AI.Extension.WPF.Mcp;

/// <summary>把 WPF 进程内的 MCP Server 通过命名管道暴露给 stdio Bridge。</summary>
public interface IAiMcpPipeExposureService : IAsyncDisposable
{
    /// <summary>是否已启动管道监听。</summary>
    bool IsRunning { get; }

    /// <summary>当前管道名。</summary>
    string PipeName { get; }

    /// <summary>Bridge exe 的绝对路径。</summary>
    string BridgePath { get; }

    /// <summary>状态文案（等待连接/已连接/错误）。</summary>
    string StatusText { get; }

    /// <summary>状态变化。</summary>
    event Action? StatusChanged;

    /// <summary>启动管道监听（在后台等待 Bridge 连接）。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>停止管道监听。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

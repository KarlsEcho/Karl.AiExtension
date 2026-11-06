using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AI.Extension.Mcp;

/// <summary>
/// 进程内 MCP Server ⇄ Client 桥。
/// <para>
/// 桌面端不需要把 MCP Server 跑成子进程：这里用一对内存管道把
/// 「宿主 DI 容器里的 MCP Server」与「聊天用的 MCP Client」直接对接。
/// 好处是工具可以直接拿到应用内的单例（ViewModel / 仓储），
/// 又保留了完整 MCP 协议边界（工具清单、参数 Schema、调用语义都由协议/微软 SDK 保证）。
/// </para>
/// </summary>
public sealed class McpServerBridge : IAsyncDisposable
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private McpClient? _client;
    private bool _disposed;

    /// <summary>服务端输入流（客户端 → 服务端）。</summary>
    public Stream ServerInputStream => _clientToServer.Reader.AsStream();

    /// <summary>服务端输出流（服务端 → 客户端）。</summary>
    public Stream ServerOutputStream => _serverToClient.Writer.AsStream();

    /// <summary>是否已建立 MCP 会话。</summary>
    public bool IsConnected => _client is not null;

    /// <summary>MCP 客户端（连接后可用）。</summary>
    public McpClient Client => _client
        ?? throw new InvalidOperationException("MCP 客户端尚未连接，请先调用 ConnectAsync。");

    /// <summary>服务端暴露的工具清单（连接后可用）。</summary>
    public IReadOnlyList<McpClientTool> Tools { get; private set; } = Array.Empty<McpClientTool>();

    /// <summary>建立进程内 MCP 会话并拉取工具清单。</summary>
    public async Task<IReadOnlyList<McpClientTool>> ConnectAsync(
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_client is not null)
            {
                return Tools;
            }

            var transport = new StreamClientTransport(
                _clientToServer.Writer.AsStream(),
                _serverToClient.Reader.AsStream(),
                loggerFactory);

            _client = await McpClient
                .CreateAsync(transport, clientOptions: null, loggerFactory, cancellationToken)
                .ConfigureAwait(false);

            Tools = (await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).ToList();
            return Tools;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        await _clientToServer.Writer.CompleteAsync().ConfigureAwait(false);
        await _serverToClient.Writer.CompleteAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }
}

/// <summary>把 <see cref="McpServerBridge"/> 接到 MCP Server 上的扩展方法。</summary>
public static class McpServerBridgeExtensions
{
    /// <summary>使用进程内双工流作为 MCP Server 的传输层。</summary>
    public static IMcpServerBuilder WithInProcessStreamTransport(this IMcpServerBuilder builder, McpServerBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(bridge);

        return builder.WithStreamServerTransport(bridge.ServerInputStream, bridge.ServerOutputStream);
    }
}

using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace AI.Extension.WPF.Mcp;

/// <summary>
/// 在 WPF 进程内启动 MCP Server，并把 stdio 传输委托给命名管道：
/// AiMcpStdioBridge.exe ⇄ NamedPipe ⇄ 本服务 ⇄ MCP Server（工具仍在 WPF 进程执行）。
/// </summary>
public sealed class AiMcpPipeExposureService : IAiMcpPipeExposureService
{
    private readonly IServiceProvider _services;
    private readonly AiMcpExposureOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private NamedPipeServerStream? _activePipe;
    private string _statusText = "尚未启动";

    /// <summary>构造服务。</summary>
    public AiMcpPipeExposureService(IServiceProvider services, AiMcpExposureOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public bool IsRunning => _acceptLoop is { IsCompleted: false };

    /// <inheritdoc />
    public string PipeName => _options.StdioPipeName;

    /// <inheritdoc />
    public string BridgePath => _options.StdioBridgePath
        ?? Path.Combine(AppContext.BaseDirectory, "AiMcpStdioBridge.exe");

    /// <inheritdoc />
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusText = value;
            StatusChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public event Action? StatusChanged;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _acceptLoop = Task.Run(() => AcceptLoopAsync(token), CancellationToken.None);
            StatusText = $"等待 Bridge 连接（管道：{PipeName}）";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;
        NamedPipeServerStream? pipe;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cts = _cts;
            loop = _acceptLoop;
            pipe = _activePipe;

            _cts = null;
            _acceptLoop = null;
            _activePipe = null;
        }
        finally
        {
            _gate.Release();
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();

        try
        {
            pipe?.Dispose();
        }
        catch
        {
            // 忽略关闭时的管道异常
        }

        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 超时/取消时忽略，进程退出会回收
            }
        }

        cts.Dispose();
        StatusText = "已关闭";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                _options.StdioPipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            lock (_gate)
            {
                _activePipe = pipe;
            }

            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception exception)
            {
                pipe.Dispose();
                StatusText = $"等待连接失败：{exception.Message}";
                continue;
            }

            StatusText = "Bridge 已连接，MCP Server 已启动";

            IHost? host = null;
            try
            {
                host = BuildMcpHost(pipe);
                await host.StartAsync(cancellationToken).ConfigureAwait(false);

                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常停止
            }
            catch (Exception exception)
            {
                StatusText = $"stdio 暴露错误：{exception.Message}";
            }
            finally
            {
                if (host is not null)
                {
                    try
                    {
                        await host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 忽略停止异常
                    }

                    host.Dispose();
                }

                pipe.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                StatusText = $"等待 Bridge 连接（管道：{PipeName}）";
            }
        }
    }

    private IHost BuildMcpHost(Stream pipe)
    {
        AiExtensionOptions options = _services.GetService<AiExtensionOptions>()
            ?? throw new InvalidOperationException("未找到 AI 扩展配置：请先通过 AddAiExtensionWpf 接入。");

        IReadOnlyList<Assembly> toolsAssemblies = options.McpToolAssemblies;
        if (toolsAssemblies.Count == 0)
        {
            throw new InvalidOperationException("未配置 MCP 工具程序集：请先调用 UseMcpToolsFrom。");
        }

        JsonSerializerOptions serializerOptions = options.ToolSerializerOptions
            ?? AiExtensionJson.CreateToolSerializerOptions();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        foreach (Assembly toolsAssembly in toolsAssemblies)
        {
            ForwardToolDependencies(builder.Services, toolsAssembly, _services);
        }

        IMcpServerBuilder mcp = builder.Services.AddMcpServer();
        foreach (Assembly toolsAssembly in toolsAssemblies)
        {
            mcp.WithToolsFromAssembly(toolsAssembly, serializerOptions);
        }

        mcp.WithStreamServerTransport(pipe, pipe);

        return builder.Build();
    }

    /// <summary>把工具构造函数依赖从宿主容器转发到 MCP 子容器（按实例注册）。</summary>
    private static void ForwardToolDependencies(IServiceCollection target, Assembly toolsAssembly, IServiceProvider source)
    {
        foreach (Type toolType in toolsAssembly.GetTypes())
        {
            if (toolType.GetCustomAttribute<McpServerToolTypeAttribute>() is null)
            {
                continue;
            }

            foreach (ConstructorInfo constructor in toolType.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    Type dependencyType = parameter.ParameterType;
                    if (target.Any(descriptor => descriptor.ServiceType == dependencyType))
                    {
                        continue;
                    }

                    object? instance = source.GetService(dependencyType);
                    if (instance is not null)
                    {
                        target.AddSingleton(dependencyType, instance);
                    }
                }
            }
        }
    }
}

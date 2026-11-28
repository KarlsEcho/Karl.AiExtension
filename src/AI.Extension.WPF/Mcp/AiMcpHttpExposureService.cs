#if NET10_0_OR_GREATER
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace AI.Extension.WPF.Mcp;

/// <summary>默认实现：同进程内启动一个只监听 127.0.0.1 的 Kestrel，挂载 MCP 的 Streamable HTTP 端点。</summary>
public sealed class AiMcpHttpExposureService : IMcpHttpExposureService
{
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _app;

    /// <summary>构造服务。</summary>
    public AiMcpHttpExposureService(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <inheritdoc />
    public bool IsRunning => _app is not null;

    /// <inheritdoc />
    public string? EndpointUrl { get; private set; }

    /// <inheritdoc />
    public async Task<string> StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间。");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_app is not null)
            {
                return EndpointUrl!;
            }

            AiExtensionOptions options = _services.GetService<AiExtensionOptions>()
                ?? throw new InvalidOperationException("未找到 AI 扩展配置：请先通过 AddAiExtension/AddAiExtensionWpf 接入。");
            IReadOnlyList<Assembly> toolsAssemblies = options.McpToolAssemblies;
            if (toolsAssemblies.Count == 0)
            {
                throw new InvalidOperationException("未配置 MCP 工具程序集：请先调用 options.UseMcpToolsFrom(typeof(XxxTools).Assembly)。");
            }

            JsonSerializerOptions serializerOptions = options.ToolSerializerOptions
                ?? AiExtensionJson.CreateToolSerializerOptions();

            var builder = WebApplication.CreateSlimBuilder();

            // 独立宿：避免与 WPF 宿主的日志提供程序（部分环境 EventLog 不可写）互相影响
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            // 工具类往往依赖宿主里的单例（契约桥接、UI 上下文日志等），
            // 这里把工具构造函数需要的依赖从宿主 DI 取实例转发进子容器。
            foreach (Assembly toolsAssembly in toolsAssemblies)
            {
                ForwardToolDependencies(builder.Services, toolsAssembly, _services);
            }

            IMcpServerBuilder mcp = builder.Services.AddMcpServer();
            foreach (Assembly toolsAssembly in toolsAssemblies)
            {
                mcp.WithToolsFromAssembly(toolsAssembly, serializerOptions);
            }

            mcp.WithHttpTransport(transportOptions => transportOptions.Stateless = true);

            WebApplication app = builder.Build();
            app.MapMcp();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            _app = app;
            EndpointUrl = $"http://127.0.0.1:{port}";

            return EndpointUrl;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WebApplication? app = _app;
            _app = null;
            EndpointUrl = null;

            if (app is not null)
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    /// <summary>把工具构造函数依赖从宿主容器转发到 HTTP 子容器（按实例注册）。</summary>
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
#endif


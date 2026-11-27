using Microsoft.Extensions.Hosting;

namespace AI.Extension.WPF.Mcp;

/// <summary>按 <see cref="AiMcpExposureOptions"/> 在宿主启动时自动开启 MCP 对外暴露。</summary>
internal sealed class AiMcpExposureHostedService : IHostedService
{
    private readonly AiMcpExposureOptions _options;
    private readonly IMcpHttpExposureService _http;
    private readonly IAiMcpPipeExposureService _pipe;

    public AiMcpExposureHostedService(
        AiMcpExposureOptions options,
        IMcpHttpExposureService http,
        IAiMcpPipeExposureService pipe)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoStart)
        {
            return;
        }

        if (_options.Mode.HasFlag(AiMcpExposureMode.Http))
        {
            try
            {
                await _http.StartAsync(_options.HttpPort, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 自动开启失败不阻塞应用启动；诊断面板可手动重试
            }
        }

        if (_options.Mode.HasFlag(AiMcpExposureMode.Stdio))
        {
            try
            {
                await _pipe.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 同上
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_http.IsRunning)
        {
            try
            {
                await _http.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 停止失败忽略
            }
        }

        if (_pipe.IsRunning)
        {
            try
            {
                await _pipe.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 停止失败忽略
            }
        }
    }
}

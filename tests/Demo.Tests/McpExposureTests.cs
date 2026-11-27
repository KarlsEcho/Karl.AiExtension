using AI.Extension;
using AI.Extension.WPF.Mcp;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Tests;

/// <summary>MCP 暴露配置与 stdio 管道服务的注册测试。</summary>
public class McpExposureTests
{
    [Fact]
    public async Task Wpf外壳默认不暴露_但注册了stdio服务()
    {
        var services = new ServiceCollection();
        services.AddAiExtensionWpf();

        await using ServiceProvider provider = services.BuildServiceProvider();

        AiMcpExposureOptions options = provider.GetRequiredService<AiMcpExposureOptions>();
        Assert.Equal(AiMcpExposureMode.None, options.Mode);

        IAiMcpPipeExposureService pipe = provider.GetRequiredService<IAiMcpPipeExposureService>();
        Assert.Equal("Karl.AiExtension.Mcp", pipe.PipeName);
        Assert.EndsWith("AiMcpStdioBridge.exe", pipe.BridgePath, StringComparison.OrdinalIgnoreCase);
        Assert.False(pipe.IsRunning);
    }
}



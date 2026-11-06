using AI.Extension;
using AI.Extension.Generated.Demo.Contract;
using AI.Extension.Mcp;
using AI.Extension.Providers;
using Demo.Contract;
using Demo.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Demo.Tests;

/// <summary>
/// 一键接入门面测试：宿主只传「工具程序集」，框架应自动完成
/// 契约桥接注册、进程内 MCP Server 注册与自动启动服务注册。
/// </summary>
public sealed class OneShotRegistrationTests
{
    [Fact]
    public async Task AddAiExtension_AutoRegistersBridgesAndInProcessMcp()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var viewModel = new OrdersViewModel(
            new Demo.Model.InMemoryOrderRepository(),
            new UiActivityTracker(),
            new ImmediateUiDispatcher());
        services.AddSingleton<IOrderService>(viewModel);

        services.AddAiExtension(options =>
        {
            options.UseMcpToolsFrom(Demo.MCP.DemoMcpExtensions.Assembly);
            options.ProviderStore = new InMemoryAiProviderStore();
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        // 生成器写在工具程序集上的标记被门面读取 -> 桥接自动注册
        Assert.NotNull(provider.GetService<OrderServiceBridge>());
        // 进程内 MCP 会话 + 供应商管理自动注册
        Assert.NotNull(provider.GetService<McpServerBridge>());
        Assert.NotNull(provider.GetService<IAiProviderManager>());
        // 自动启动服务（StartAsync 时初始化供应商 + 连接 MCP Client）
        Assert.NotEmpty(provider.GetServices<IHostedService>());
    }
}


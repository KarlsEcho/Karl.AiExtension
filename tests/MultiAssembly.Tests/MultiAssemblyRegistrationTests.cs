using AI.Extension;
using AI.Extension.Chat;
using AI.Extension.Mcp;
using Demo.Contract;
using Demo.SecondTools;
using Demo.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace MultiAssembly.Tests;

/// <summary>多程序集/插件注册测试：工具追加、契约目录聚合、MCP 配置回调。</summary>
public class MultiAssemblyRegistrationTests
{
    [Fact]
    public async Task 多工具程序集_可追加并触发ConfigureMcpServer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOrderService>(CreateOrderService());
        services.AddSingleton<ISecondService>(new FakeSecondService());

        bool configured = false;
        services.AddAiExtension(options =>
        {
            options.UseMcpToolsFrom(Demo.MCP.DemoMcpExtensions.Assembly);
            options.AddMcpToolsFrom(typeof(SecondPluginTools).Assembly);
            options.ConfigureMcpServer = _ => configured = true;
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        AiExtensionOptions options = provider.GetRequiredService<AiExtensionOptions>();
        Assert.Equal(2, options.McpToolAssemblies.Count);
        Assert.True(configured);
        Assert.NotNull(provider.GetService<McpServerBridge>());
    }

    [Fact]
    public async Task 多程序集契约目录_聚合所有桥接()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOrderService>(CreateOrderService());
        services.AddSingleton<ISecondService>(new FakeSecondService());

        services.AddAiExtensionContracts(
            Demo.MCP.DemoMcpExtensions.Assembly,
            typeof(SecondPluginTools).Assembly);

        await using ServiceProvider provider = services.BuildServiceProvider();
        AiContractCatalog catalog = provider.GetRequiredService<AiContractCatalog>();

        Assert.Contains(catalog.Contracts, contract => contract.ContractName == "OrderService");
        Assert.Contains(catalog.Contracts, contract => contract.ContractName == "SecondService");
    }

    private static OrdersViewModel CreateOrderService()
        => new(
            new Demo.Model.InMemoryOrderRepository(),
            new UiActivityTracker(),
            new ImmediateUiDispatcher());

    private sealed class FakeSecondService : ISecondService
    {
        public string Ping(string name) => "pong:" + name;
    }
}

using AI.Extension;
using AI.Extension.Generated;
using AI.Extension.Generated.Demo.Contract;
using Demo.Contract;
using Demo.Model;
using Demo.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Tests;

/// <summary>验证「契约 → 源生成器 → 桥接 → DI」这条链路。</summary>
public sealed class ContractGenerationTests
{
    [Fact]
    public void Describe_ReturnsOrderServiceContractWithFourMethods()
    {
        IReadOnlyList<AiContractDescriptor> descriptors = AiContractBridgeRegistration.Describe();

        AiContractDescriptor contract = Assert.Single(descriptors);
        Assert.Equal("OrderService", contract.ContractName);
        Assert.Equal("Demo.Contract.IOrderService", contract.ContractFullName);
        Assert.Equal(4, contract.Methods.Count);
        Assert.Equal(
            new[] { "order_search", "order_get", "order_cancel", "order_summary" },
            contract.Methods.Select(static method => method.SuggestedToolName).ToArray());
    }

    [Fact]
    public async Task Bridge_ForwardsCallsToContractImplementation()
    {
        var repository = InMemoryOrderRepository.FromOrders(
        [
            new Order { Id = "SO-2001", CustomerName = "测试客户", Amount = 100m, Status = OrderStatus.Paid },
        ]);

        var viewModel = new OrdersViewModel(repository, new UiActivityTracker(), new ImmediateUiDispatcher());

        using var provider = BuildProvider(viewModel);

        var bridge = provider.GetRequiredService<OrderServiceBridge>();
        var catalog = provider.GetRequiredService<AiContractCatalog>();

        Assert.Equal("OrderService", bridge.ContractName);
        Assert.Single(catalog.Contracts);
        Assert.Equal(4, bridge.Methods.Count);

        IReadOnlyList<Order> matches = await bridge.SearchAsync("测试", null, CancellationToken.None);

        Assert.Single(matches);
        Assert.Single(viewModel.Orders);
    }

    private static ServiceProvider BuildProvider(IOrderService orderService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(orderService);
        services.AddAiContractBridges();
        return services.BuildServiceProvider();
    }
}

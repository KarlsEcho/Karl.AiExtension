using System.IO;
using System.Windows;
using AI.Extension.Mcp;
using Demo.Core;
using Demo.View;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Demo.App;

/// <summary>
/// WPF 宿主入口。这里是最薄的一层：建 Host → 启动（含进程内 MCP Server）→ 连 MCP Client → 显示窗口。
/// 具体的 DI 组装全部收敛在 <see cref="DemoBootstrap.AddDemoCoreForWpf"/> 里，类似 ASP.NET Core 的 Startup / Swagger 注册方式。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(e.Args);
            builder.Logging.ClearProviders();

            // 组合根：WPF 外壳 + 业务 + 生成器桥接 + 进程内 MCP Server + 工具
            builder.Services.AddDemoCoreForWpf();
            builder.Services.AddSingleton<MainWindow>();

            _host = builder.Build();

            // 供应商配置必须先于窗口/对话加载：首次运行会写入内置预设，之后所有修改即时生效
            await _host.Services.GetRequiredService<AI.Extension.Providers.IAiProviderManager>().InitializeAsync();

            await _host.StartAsync();

            // 启动进程内 MCP 会话：MCP Server 与 Client 通过内存管道直连
            McpServerBridge bridge = _host.Services.GetRequiredService<McpServerBridge>();
            await bridge.ConnectAsync(_host.Services.GetService<ILoggerFactory>());

            if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
            {
                await RunSmokeAsync(_host);
                Shutdown(0);
                return;
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            // 启动失败也要留下可诊断的痕迹（WinExe 没有控制台输出）
            try
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, "startup-error.log"),
                    exception.ToString());
            }
            catch (Exception)
            {
                // 日志写入失败时忽略，仍然弹窗
            }

            MessageBox.Show(
                $"启动失败：{exception.Message}\n\n{exception}",
                "Karl.AiExtension",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// 无界面自检：验证 WPF 组合根（WpfUiDispatcher + 进程内 MCP + 对话链路）在真实容器里可用。
    /// 运行：<c>Demo.App.exe --smoke</c>，结果写入工作目录下的 <c>smoke.log</c>。
    /// </summary>
    private static async Task RunSmokeAsync(IHost host)
    {
        var lines = new List<string>();
        try
        {
            var orders = host.Services.GetRequiredService<Demo.ViewModel.OrdersViewModel>();
            var catalog = host.Services.GetRequiredService<AI.Extension.AiContractCatalog>();
            var bridge = host.Services.GetRequiredService<McpServerBridge>();
            var session = host.Services.GetRequiredService<AI.Extension.Chat.ChatSession>();
            var providers = host.Services.GetRequiredService<AI.Extension.Providers.IAiProviderManager>();

            await orders.LoadAsync();
            lines.Add($"UICONTEXT: {host.Services.GetRequiredService<AI.Extension.IUiDispatcher>().GetType().Name}");
            lines.Add($"PROVIDERS: {providers.Providers.Count} | ACTIVE: {providers.ActiveModelLabel}");
            lines.Add($"CONTRACTS: {catalog.Contracts.Count}");
            lines.Add($"TOOLS: {string.Join(",", bridge.Tools.Select(static tool => tool.Name))}");
            lines.Add($"ORDERS: {orders.Orders.Count}");

            // 构造主窗口以验证 XAML/绑定/依赖注入组合是否完整（不显示）
            _ = host.Services.GetRequiredService<MainWindow>();
            lines.Add("UI: MainWindow 构造成功");

            // 验证桌面端「写操作」链路：MCP 工具 -> 语义封装 -> ViewModel -> UI 集合
            var repository = host.Services.GetRequiredService<Demo.Model.IOrderRepository>();
            int beforeCount = repository.GetAll().Count;
            var createResult = await bridge.Client.CallToolAsync(
                "order_create",
                new Dictionary<string, object?>
                {
                    ["customerName"] = "冒烟测试客户",
                    ["amount"] = 66m,
                    ["remark"] = "smoke",
                });
            string createText = string.Join(
                "\n",
                createResult.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(static block => block.Text));
            lines.Add($"WRITE_CREATE: {createText}");
            lines.Add($"ORDERS_AFTER_CREATE: repo={repository.GetAll().Count} ui={orders.Orders.Count}");

            Demo.Model.Order? createdOrder = repository.GetAll()
                .FirstOrDefault(static order => order.CustomerName == "冒烟测试客户");
            if (createdOrder is not null)
            {
                await bridge.Client.CallToolAsync(
                    "order_delete",
                    new Dictionary<string, object?> { ["orderId"] = createdOrder.Id, ["confirm"] = true });
            }

            lines.Add($"ORDERS_AFTER_DELETE: repo={repository.GetAll().Count} ui={orders.Orders.Count} (before={beforeCount})");

            AI.Extension.Chat.AiChatReply reply = await session.SendAsync("帮我统计一下现在的订单情况");
            lines.Add($"REPLY: {reply.Text.ReplaceLineEndings(" ")}");
            lines.Add($"TOOLCALLS: {string.Join(",", reply.ToolCalls.Select(static trace => trace.ToolName))}");
            lines.Add("SMOKE: OK");
        }
        catch (Exception exception)
        {
            lines.Add($"SMOKE: FAIL {exception}");
        }

        await File.WriteAllLinesAsync("smoke.log", lines);
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // 退出阶段不再打断用户
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }

        base.OnExit(e);
    }
}

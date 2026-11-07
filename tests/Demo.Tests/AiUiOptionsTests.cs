using System.Windows.Controls;
using AI.Extension;
using AI.Extension.WPF.Chat;
using AI.Extension.WPF.Diagnostics;
using AI.Extension.WPF.Providers;
using AI.Extension.WPF.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Tests;

/// <summary>UI 可替换配置测试：槽位类型注册 + 视图工厂注册。</summary>
public class AiUiOptionsTests
{
    [Fact]
    public void Ui选项_泛型方法写入对应槽位()
    {
        var options = new AiExtensionUiOptions();

        options.UseChatShell<FakeChatShell>();
        options.UseConversation<FakeConversation>();
        options.UseProviderManager<FakeProviderManager>();
        options.UseDiagnostics<FakeDiagnostics>();

        Assert.Equal(typeof(FakeChatShell), options.ChatShellView);
        Assert.Equal(typeof(FakeConversation), options.ConversationView);
        Assert.Equal(typeof(FakeProviderManager), options.ProviderManagerView);
        Assert.Equal(typeof(FakeDiagnostics), options.DiagnosticsView);
    }

    [Fact]
    public void Wpf外壳注册_提供默认视图工厂()
    {
        var services = new ServiceCollection();
        services.AddAiExtensionWpf();

        using ServiceProvider provider = services.BuildServiceProvider();
        IAiViewFactory factory = provider.GetRequiredService<IAiViewFactory>();
        Assert.IsType<AiViewFactory>(factory);
        Assert.IsType<AiExtensionUiOptions>(provider.GetRequiredService<AiExtensionUiOptions>());
    }

    // 仅用于类型约束测试，不实例化（避免测试线程需要 STA/WPF Application）
    private sealed class FakeChatShell : UserControl, IAiView<ChatTabsViewModel>
    {
        public void Attach(ChatTabsViewModel viewModel)
        {
        }
    }

    private sealed class FakeConversation : UserControl, IAiView<ChatPanelViewModel>
    {
        public void Attach(ChatPanelViewModel viewModel)
        {
        }
    }

    private sealed class FakeProviderManager : UserControl, IAiView<ProviderManagerViewModel>
    {
        public void Attach(ProviderManagerViewModel viewModel)
        {
        }
    }

    private sealed class FakeDiagnostics : UserControl, IAiView<AiDiagnosticsViewModel>
    {
        public void Attach(AiDiagnosticsViewModel viewModel)
        {
        }
    }
}

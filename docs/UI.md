# 自定义 AI UI

> 结论：**框架对 UI 的契约就是 public 的 ViewModel**。
> 想改 UI，两种方式：整块替换某个槽位，或者直接把内置视图的 XAML 复制到你的工程按需改。
> 框架不再提供「模板 key / 局部模板替换」这一层——那会把每个 key 变成公共契约，维护成本高于收益。

---

## 一、你能拿到的 ViewModel

| ViewModel | 职责 | 注册方式 |
| --- | --- | --- |
| `ChatTabsViewModel` | 多会话标签容器：`Tabs`、`SelectedTab`、新建/关闭/归档/删除、打开会话 | `AddAiExtensionWpf()` 自动注册单例 |
| `ChatPanelViewModel` | 单个会话：`Messages`、`InputText`、`SendCommand`、`CancelSendCommand`、`StatusText`、`Title` | 由 `ChatTabsViewModel` 创建（不在容器里单例） |
| `ProviderManagerViewModel` | 供应商/模型增删改查、测试连接、拉取模型 | 自动注册单例 |
| `AiDiagnosticsViewModel` | 契约/工具/活动日志 + 对外暴露 MCP 开关 | 自动注册单例 |

这些都是 `public` 类型，成员对宿主可见；直接绑定即可。

---

## 二、方式 A：整槽位替换（推荐用于「换壳/换布局」）

实现 `IAiView<TViewModel>`（或继承薄基类 `AiViewBase<TViewModel>`），再在 DI 里指到对应槽位：

```csharp
// 1. 视图（XAML 根仍是 UserControl，或改成 AiViewBase<ChatTabsViewModel>）
public partial class MyChatShell : UserControl, IAiView<ChatTabsViewModel>
{
    public MyChatShell() => InitializeComponent();

    public void Attach(ChatTabsViewModel viewModel) => DataContext = viewModel;
}
```

```csharp
// 2. 注册
services.AddAiExtensionWpf(
    options => options.UseMcpToolsFrom(typeof(OrderTools).Assembly),
    ui =>
    {
        ui.UseChatShell<MyChatShell>();          // 换对话页
        ui.UseConversation<MyConversation>();     // 只换消息区
        ui.UseProviderManager<MyProviderView>();
        ui.UseDiagnostics<MyDiagnosticsView>();
    });
```

宿主 XAML 用宿主控件承载：

```xml
<ui:AiChatHost DataContext="{Binding Chat}" />
<ui:AiProviderManagerHost DataContext="{Binding Providers}" />
<ui:AiDiagnosticsHost DataContext="{Binding Diagnostics}" />
```

- 泛型 `UseXxx<T>()` 会编译期检查 `T` 是 `FrameworkElement` 且实现对应 `IAiView<VM>`；
- 视图构造支持 DI：如果容器里注册了该视图实例，工厂优先用注册的实例；否则 `ActivatorUtilities.CreateInstance`；
- 不配置就使用内置视图，行为与之前一致。

---

## 三、方式 B：复制内置视图改造（推荐用于「只改局部」）

内置视图就是普通 WPF UserControl，**只依赖 public ViewModel**，没有任何隐藏契约。想改局部时：

1. 把 `src/AI.Extension.WPF/Chat/ChatConversationView.xaml`（消息列表/输入区）或
   `AiChatPanel.xaml`（会话标签外壳）复制到你自己的工程；
2. 改 `x:Class` 的命名空间；
3. 注册到槽位（方式 A）或直接替换你 XAML 里的控件。

这样比框架维护一堆模板 key 更灵活：你想改哪个控件就改哪个，不受框架后续版本影响。

---

## 四、注意事项

1. **不要重建/释放 ViewModel**：`ChatTabsViewModel` 是单例，`ChatPanelViewModel` 由它管理；View 只负责呈现与转发命令；
2. **View 建议保留无参构造**（WPF UserControl 默认满足），否则 XAML/反射创建会失败；需要额外服务时注册到 DI；
3. **`Attach(vm)` 后 DataContext 就绪**，你的 XAML 直接绑定即可；
4. 自定义 View 里不要直接 `new` 框架 ViewModel，统一从 DataContext / DI 取；
5. 如果未来框架升级导致 VM 成员变化，以 README/变更记录为准；自定义 XAML 不受影响，除非用到被移除的成员。

---

## 五、为什么不做「局部模板替换」

- 每个模板 key 都会变成对外承诺的公共 API，改 key/模板结构就是 breaking change；
- WPF 原生已有 `DataTemplate`/`Style`/`ControlTemplate` 机制，宿主在自己的 `App.xaml` 里覆盖内置控件样式即可，不需要框架再包一层；
- 对框架来说，**少一层抽象 = 少一份维护和版本负担**。

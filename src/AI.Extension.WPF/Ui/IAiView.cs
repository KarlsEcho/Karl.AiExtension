namespace AI.Extension.WPF.Ui;

/// <summary>
/// AI 视图契约：框架通过 <see cref="Attach"/> 把对应的 ViewModel 交给视图。
/// <para>
/// 泛型参数声明该视图绑定哪个 ViewModel，注册时由泛型约束在编译期检查。
/// 视图本身必须是 <see cref="System.Windows.FrameworkElement"/>（通常是 UserControl）。
/// </para>
/// </summary>
/// <typeparam name="TViewModel">视图对应的 ViewModel 类型。</typeparam>
public interface IAiView<in TViewModel>
{
    /// <summary>挂载 ViewModel（一般实现为 <c>DataContext = viewModel;</c>）。</summary>
    void Attach(TViewModel viewModel);
}

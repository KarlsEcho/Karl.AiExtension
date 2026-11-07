using System.Windows.Controls;

namespace AI.Extension.WPF.Ui;

/// <summary>
/// 视图基类（可选）：提供 <see cref="Attach"/> 的默认实现，只做 <c>DataContext</c> 赋值，
/// 不接管布局，子类照常写 XAML。
/// <para>
/// 用法（XAML 根元素写基类）：
/// <code>
/// &lt;ui:AiViewBase x:Class="MyApp.MyChatShell" ... /&gt;
/// </code>
/// 或者不使用基类，直接在 UserControl 上实现 <see cref="IAiView{TViewModel}"/>。
/// </para>
/// </summary>
/// <typeparam name="TViewModel">视图对应的 ViewModel 类型。</typeparam>
public abstract class AiViewBase<TViewModel> : UserControl, IAiView<TViewModel>
{
    /// <inheritdoc />
    public virtual void Attach(TViewModel viewModel) => DataContext = viewModel;
}

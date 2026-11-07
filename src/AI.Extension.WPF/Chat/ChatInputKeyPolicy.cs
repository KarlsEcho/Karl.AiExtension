using System.Windows.Input;

namespace AI.Extension.WPF.Chat;

/// <summary>
/// 聊天输入框的按键策略（纯函数，便于单测）。
/// <para>
/// 中文输入法激活时，WPF 会把 Enter 标记为 <see cref="Key.ImeProcessed"/>（真实键放在
/// <see cref="KeyEventArgs.ImeProcessedKey"/>），只判断 <see cref="Key.Enter"/> 会漏掉发送。
/// 策略：组词中（Enter 用于上屏）不发送；Shift+Enter 换行；其余 Enter 发送。
/// </para>
/// </summary>
public static class ChatInputKeyPolicy
{
    /// <summary>判断本次按键是否应当触发「发送」。</summary>
    /// <param name="key">按键（可能是 <see cref="Key.ImeProcessed"/>）。</param>
    /// <param name="imeProcessedKey">输入法处理前的真实按键。</param>
    /// <param name="modifiers">当前修饰键。</param>
    /// <param name="isImeComposing">是否正在输入法组词。</param>
    public static bool ShouldSendOnEnter(Key key, Key imeProcessedKey, ModifierKeys modifiers, bool isImeComposing)
    {
        if (isImeComposing)
        {
            // 组词中的 Enter 属于输入法「上屏」，不能当作发送
            return false;
        }

        bool isEnter = key == Key.Enter
            || (key == Key.ImeProcessed && imeProcessedKey == Key.Enter);
        if (!isEnter)
        {
            return false;
        }

        // Shift+Enter 换行，交给 TextBox 默认行为
        return (modifiers & ModifierKeys.Shift) == 0;
    }
}

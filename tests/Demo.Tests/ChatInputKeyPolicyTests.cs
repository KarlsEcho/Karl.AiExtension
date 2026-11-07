using System.Windows.Input;
using AI.Extension.WPF.Chat;

namespace Demo.Tests;

/// <summary>输入框「Enter 发送 / Shift+Enter 换行」的策略测试（含中文输入法场景）。</summary>
public class ChatInputKeyPolicyTests
{
    [Fact]
    public void 普通回车_应当发送()
    {
        Assert.True(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.Enter, Key.None, ModifierKeys.None, isImeComposing: false));
    }

    [Fact]
    public void Shift加回车_应当换行而不是发送()
    {
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.Enter, Key.None, ModifierKeys.Shift, isImeComposing: false));
    }

    [Fact]
    public void 输入法激活时回车被标记为ImeProcessed_没有组词时仍应发送()
    {
        // 中文输入法激活但没有候选词时，WPF 把 Enter 标记为 ImeProcessed，真实键在 ImeProcessedKey
        Assert.True(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.ImeProcessed, Key.Enter, ModifierKeys.None, isImeComposing: false));
    }

    [Fact]
    public void 输入法组词中的回车_应当上屏而不是发送()
    {
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.ImeProcessed, Key.Enter, ModifierKeys.None, isImeComposing: true));
    }

    [Fact]
    public void 输入法组词中的Shift加回车_不发送()
    {
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.ImeProcessed, Key.Enter, ModifierKeys.Shift, isImeComposing: true));
    }

    [Fact]
    public void 输入法处理的其他按键_不发送()
    {
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.ImeProcessed, Key.Space, ModifierKeys.None, isImeComposing: false));
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.ImeProcessed, Key.Space, ModifierKeys.None, isImeComposing: true));
    }

    [Fact]
    public void 非回车按键_不发送()
    {
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.A, Key.None, ModifierKeys.None, isImeComposing: false));
        Assert.False(ChatInputKeyPolicy.ShouldSendOnEnter(
            Key.Escape, Key.None, ModifierKeys.None, isImeComposing: false));
    }
}

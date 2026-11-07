using System.Windows.Controls;

namespace AI.Extension.WPF.Chat;

/// <summary>AI 对话外壳：多会话标签容器（每个标签是一个独立的 <see cref="ChatConversationView"/>）。</summary>
public partial class AiChatPanel : UserControl
{
    /// <summary>构造面板。</summary>
    public AiChatPanel()
    {
        InitializeComponent();
    }
}

using AI.Extension.Chat;
using Microsoft.AspNetCore.Components;

namespace AI.Extension.Blazor;

/// <summary>
/// 可复用聊天面板的 code-behind。
/// <para>
/// 设计要点：
/// 1) 只依赖 <see cref="ChatSession"/>，不直接接触 MCP 客户端：工具循环、错误回灌、历史管理
///    全部复用 AI.Extension.Core，Blazor 外壳与 WPF 外壳共享同一段会话核心；
/// 2) <see cref="ChatSession.SendAsync"/> 与 <see cref="ChatSession.ToolCallCompleted"/>
///    都可能发生在非渲染线程（核心层内部大量 ConfigureAwait(false)，默认 IUiDispatcher 又是
///    ImmediateUiDispatcher），因此所有跨线程的状态更新统一通过
///    <see cref="ComponentBase.InvokeAsync(Action)"/> 封送回 Blazor 渲染上下文；
/// 3) 组件只保存「当前页面」的展示状态，真正的对话历史仍留在 ChatSession（单例）里。
/// </para>
/// </summary>
public partial class ChatPanel
{
    private readonly List<ChatTurn> _turns = new();
    private readonly List<AiToolCallTrace> _pendingTraces = new();
    private bool _disposed;

    /// <summary>对话会话（由组合根注册：AddDemoCore / AddAiChatSession）。</summary>
    [Inject]
    public ChatSession Session { get; set; } = default!;

    /// <summary>面板标题。</summary>
    [Parameter]
    public string Title { get; set; } = "AI 助手";

    /// <summary>输入框占位提示。</summary>
    [Parameter]
    public string Placeholder { get; set; } = "例如：帮我统计一下现在的订单情况";

    /// <summary>没有对话时的空状态提示。</summary>
    [Parameter]
    public string EmptyHint { get; set; } = "还没有对话。试试「帮我统计一下现在的订单情况」或「看看 SO-1002 这个订单」。";

    /// <summary>已经完成的对话轮次（仅本组件内存）。</summary>
    private IReadOnlyList<ChatTurn> Turns => _turns;

    /// <summary>本轮正在发生的工具调用（供「正在处理」状态下实时展示）。</summary>
    private IReadOnlyList<AiToolCallTrace> PendingTraces => _pendingTraces;

    /// <summary>输入框内容（@bind 双向绑定）。</summary>
    private string Input { get; set; } = string.Empty;

    /// <summary>是否正在等待模型 / 工具。</summary>
    private bool IsBusy { get; set; }

    /// <summary>上一次调用的错误提示。</summary>
    private string? ErrorMessage { get; set; }

    private bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(Input);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // 工具轨迹是实时事件；它会在 ChatSession 的后台线程上触发，因此回调内部必须 InvokeAsync。
        Session.ToolCallCompleted += OnToolCallCompleted;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (Session is not null)
        {
            Session.ToolCallCompleted -= OnToolCallCompleted;
        }
    }

    private void OnToolCallCompleted(AiToolCallTrace trace)
    {
        if (_disposed)
        {
            return;
        }

        // 关键：工具轨迹事件来自非渲染线程，直接改状态会破坏 Blazor 的渲染队列。
        _ = InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            _pendingTraces.Add(trace);
            StateHasChanged();
        });
    }

    private async Task SendAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string text = Input.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var turn = new ChatTurn(text);
        _turns.Add(turn);
        Input = string.Empty;
        ErrorMessage = null;
        IsBusy = true;
        StateHasChanged();

        try
        {
            AiChatReply reply = await Session.SendAsync(text);

            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                turn.AssistantText = reply.Text;
                turn.Traces = reply.ToolCalls;
                _pendingTraces.Clear();
                IsBusy = false;
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            await InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                ErrorMessage = $"对话失败：{exception.Message}";
                _pendingTraces.Clear();
                IsBusy = false;
                StateHasChanged();
            });
        }
    }

    private Task ClearConversationAsync()
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        Session.ClearHistory();
        _turns.Clear();
        _pendingTraces.Clear();
        ErrorMessage = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>一轮对话：用户输入 → AI 文本 → 本轮工具轨迹。</summary>
    private sealed class ChatTurn
    {
        public ChatTurn(string userText) => UserText = userText;

        public string UserText { get; }

        public string? AssistantText { get; set; }

        public IReadOnlyList<AiToolCallTrace> Traces { get; set; } = Array.Empty<AiToolCallTrace>();
    }
}

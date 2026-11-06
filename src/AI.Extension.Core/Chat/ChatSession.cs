using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AI.Extension.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AI.Extension.Chat;

/// <summary>
/// AI 对话会话：把「大模型」「MCP 工具」「UI 上下文日志」串成一条闭环。
/// <para>
/// 工具循环是显式实现的（而不是交给中间件），原因：
/// 1) 需要把每次工具调用实时上报给 UI（工具轨迹面板）；
/// 2) 需要在这里做权限/确认拦截（敏感操作先问用户）；
/// 3) 需要把失败结果以「可读错误」回灌给模型，让它自我纠正。
/// </para>
/// </summary>
public sealed class ChatSession
{
    private const int MaxPreviewLength = 400;

    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IChatClient _chatClient;
    private readonly McpServerBridge _bridge;
    private readonly IUiActivityTracker _activityTracker;
    private readonly AiChatOptions _options;
    private readonly List<ChatMessage> _history = new();
    private readonly SemaphoreSlim _turnGate = new(1, 1);

    /// <summary>构造会话。</summary>
    public ChatSession(
        IChatClient chatClient,
        McpServerBridge bridge,
        IUiActivityTracker activityTracker,
        AiChatOptions options)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _activityTracker = activityTracker ?? throw new ArgumentNullException(nameof(activityTracker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>工具调用完成事件（UI 可订阅以实时刷新轨迹）。</summary>
    public event Action<AiToolCallTrace>? ToolCallCompleted;

    /// <summary>历史消息条数。</summary>
    public int HistoryCount
    {
        get
        {
            lock (_history)
            {
                return _history.Count;
            }
        }
    }

    /// <summary>清空对话历史（UI 的「新对话」）。</summary>
    public void ClearHistory()
    {
        lock (_history)
        {
            _history.Clear();
        }
    }

    /// <summary>发送一条用户消息，返回最终回复与工具轨迹。</summary>
    public async Task<AiChatReply> SendAsync(string userInput, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userInput);
        await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendCoreAsync(userInput.Trim(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async Task<AiChatReply> SendCoreAsync(string userInput, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt()),
        };

        lock (_history)
        {
            messages.AddRange(_history);
        }

        messages.Add(new ChatMessage(ChatRole.User, userInput));

        var traces = new List<AiToolCallTrace>();
        string finalText = string.Empty;

        for (int iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chatOptions = new ChatOptions
            {
                Tools = _bridge.Tools.Cast<AITool>().ToList(),
                ToolMode = ChatToolMode.Auto,
            };

            ChatResponse response = await _chatClient
                .GetResponseAsync(messages, chatOptions, cancellationToken)
                .ConfigureAwait(false);

            messages.AddRange(response.Messages);

            List<FunctionCallContent> functionCalls = response.Messages
                .SelectMany(static message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (functionCalls.Count == 0)
            {
                finalText = response.Text ?? string.Empty;
                break;
            }

            foreach (FunctionCallContent call in functionCalls)
            {
                AiToolCallTrace trace = await InvokeToolAsync(call, cancellationToken).ConfigureAwait(false);
                traces.Add(trace);
                ToolCallCompleted?.Invoke(trace);

                messages.Add(new ChatMessage(
                    ChatRole.Tool,
                    new List<AIContent> { new FunctionResultContent(call.CallId, BuildToolPayload(trace)) }));
            }
        }

        if (string.IsNullOrWhiteSpace(finalText))
        {
            finalText = traces.Count == 0
                ? "模型没有返回内容，请检查模型配置。"
                : $"已完成 {traces.Count} 次工具调用，但模型未给出最终答复（可能达到轮次上限）。";
        }

        lock (_history)
        {
            _history.Add(new ChatMessage(ChatRole.User, userInput));
            _history.Add(new ChatMessage(ChatRole.Assistant, finalText));
        }

        _activityTracker.Record(
            UiActivityCategory.AgentAction,
            $"AI 对话完成：调用 {traces.Count} 次工具",
            traces.Count == 0 ? null : string.Join(" → ", traces.Select(static trace => trace.ToolName)));

        return new AiChatReply(finalText, traces);
    }

    private async Task<AiToolCallTrace> InvokeToolAsync(FunctionCallContent call, CancellationToken cancellationToken)
    {
        string argumentsJson = call.Arguments is null
            ? "{}"
            : JsonSerializer.Serialize(call.Arguments, TraceJsonOptions);

        McpClientTool? tool = _bridge.Tools.FirstOrDefault(
            candidate => string.Equals(candidate.Name, call.Name, StringComparison.Ordinal));

        if (tool is null)
        {
            string available = string.Join(", ", _bridge.Tools.Select(static t => t.Name));
            return new AiToolCallTrace(
                call.Name,
                argumentsJson,
                Success: false,
                ResultPreview: $"工具不存在。可用工具：{available}",
                Duration: TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            IReadOnlyDictionary<string, object?>? arguments =
                call.Arguments is null ? null : new Dictionary<string, object?>(call.Arguments);

            CallToolResult result = await tool
                .CallAsync(arguments, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            bool success = result.IsError is not true;
            string text = ExtractText(result);

            return new AiToolCallTrace(call.Name, argumentsJson, success, Truncate(text), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new AiToolCallTrace(
                call.Name,
                argumentsJson,
                Success: false,
                ResultPreview: exception.Message,
                Duration: stopwatch.Elapsed);
        }
    }

    private static string BuildToolPayload(AiToolCallTrace trace)
        => (trace.Success ? "OK: " : "ERROR: ") + trace.ResultPreview;

    private static string ExtractText(CallToolResult result)
    {
        if (result.Content is null || result.Content.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (ContentBlock block in result.Content)
        {
            if (block is TextContentBlock text)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(text.Text);
            }
        }

        return builder.ToString().Trim();
    }

    private static string Truncate(string value)
        => value.Length <= MaxPreviewLength ? value : value.Substring(0, MaxPreviewLength) + "…";

    private string BuildSystemPrompt()
    {
        string uiContext = _activityTracker.BuildContextSummary(_options.UiContextEntryCount);

        return $"""
            {_options.SystemPrompt}

            # 当前界面上下文
            以下是用户最近在界面上的操作（由 UI 活动环形日志在唤醒时读取，不是实时推送）：
            {uiContext}
            """;
    }
}

using System.Text.Json;
using AI.Extension.Chat;
using Microsoft.Extensions.AI;

namespace Demo.Core;

/// <summary>
/// 离线演示用的「假模型」：不联网、无需 API Key，用关键词规则决定调用哪个 MCP 工具。
/// <para>
/// 用途：让整套架构（Prompt → 工具选择 → MCP → 语义封装 → ViewModel → UI 同步）
/// 在没有配置大模型时也能完整跑通、可演示、可回归测试。
/// 配置了 KARL_AI_API_KEY 后，这个类不会被使用，改由真实模型驱动。
/// </para>
/// </summary>
public sealed class DemoOfflineChatClient : IChatClient
{
    private const string OfflineHint = "（离线演示模式：未配置大模型，以下由本地规则路由到 MCP 工具）";

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();

        List<ChatMessage> list = messages.ToList();
        ChatMessage last = list[^1];

        // 上一步是工具结果 → 汇总成最终答复
        if (last.Role == ChatRole.Tool)
        {
            string payload = last.Contents
                .OfType<FunctionResultContent>()
                .Select(static content => content.Result?.ToString() ?? string.Empty)
                .FirstOrDefault() ?? string.Empty;

            string answer = $"（离线演示模式）工具返回：\n{Truncate(payload, 800)}";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        string userText = list.LastOrDefault(static message => message.Role == ChatRole.User)?.Text ?? string.Empty;
        ChatMessage reply = Route(userText);
        return Task.FromResult(new ChatResponse(reply));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (ChatMessage message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Text);
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static ChatMessage Route(string userText)
    {
        string text = userText.Trim();
        string? orderId = ExtractOrderId(text);
        decimal? amount = ExtractAmount(text);

        // 0) 删除（破坏性：离线模式直接带 confirm，演示封装层的二次确认）
        if (text.Contains("删除", StringComparison.Ordinal) || text.Contains("删掉", StringComparison.Ordinal))
        {
            if (orderId is null)
            {
                return Text("请告诉我要删除哪个订单号（例如 SO-1001）。删除不可恢复，我会先让你确认。");
            }

            return CallTool("order_delete", new Dictionary<string, object?>
            {
                ["orderId"] = orderId,
                ["confirm"] = true,
            });
        }

        // 1) 取消订单
        if (text.Contains("取消", StringComparison.Ordinal))
        {
            if (orderId is null)
            {
                return Text("请告诉我要取消哪个订单号（例如 SO-1001），我会先确认再执行。");
            }

            return CallTool("order_cancel", new Dictionary<string, object?>
            {
                ["orderId"] = orderId,
                ["reason"] = "用户通过 AI 对话要求取消",
            });
        }

        // 2) 新增订单
        if (text.Contains("新增", StringComparison.Ordinal) ||
            text.Contains("创建", StringComparison.Ordinal) ||
            text.Contains("新建", StringComparison.Ordinal) ||
            text.Contains("加一个", StringComparison.Ordinal) ||
            text.Contains("下单", StringComparison.Ordinal))
        {
            if (amount is null)
            {
                return Text("请告诉我新订单的金额（例如「新增客户 A 金额 1200」）。");
            }

            return CallTool("order_create", new Dictionary<string, object?>
            {
                ["customerName"] = ExtractCustomerName(text),
                ["amount"] = amount.Value,
                ["remark"] = "通过 AI 离线演示创建",
            });
        }

        // 3) 修改订单
        if (text.Contains("修改", StringComparison.Ordinal) ||
            text.Contains("更新", StringComparison.Ordinal) ||
            text.Contains("改成", StringComparison.Ordinal) ||
            text.Contains("调整", StringComparison.Ordinal))
        {
            if (orderId is null)
            {
                return Text("请告诉我要修改哪个订单号（例如 SO-1001）。");
            }

            var arguments = new Dictionary<string, object?> { ["orderId"] = orderId };
            if (amount is not null)
            {
                arguments["amount"] = amount.Value;
            }

            arguments["remark"] = "通过 AI 离线演示修改";
            return CallTool("order_update", arguments);
        }

        // 4) 变更状态
        if (text.Contains("发货", StringComparison.Ordinal) ||
            text.Contains("完成", StringComparison.Ordinal) ||
            text.Contains("支付", StringComparison.Ordinal) ||
            text.Contains("已付", StringComparison.Ordinal))
        {
            if (orderId is null)
            {
                return Text("请告诉我要变更哪个订单的状态（例如「把 SO-1001 标记为已支付」）。");
            }

            string targetStatus = text.Contains("发货", StringComparison.Ordinal) ? "Shipped"
                : text.Contains("完成", StringComparison.Ordinal) ? "Completed"
                : "Paid";

            return CallTool("order_change_status", new Dictionary<string, object?>
            {
                ["orderId"] = orderId,
                ["status"] = targetStatus,
            });
        }

        // 5) 统计/汇总
        if (text.Contains("统计", StringComparison.Ordinal) ||
            text.Contains("汇总", StringComparison.Ordinal) ||
            text.Contains("概况", StringComparison.Ordinal) ||
            text.Contains("总览", StringComparison.Ordinal) ||
            text.Contains("多少", StringComparison.Ordinal))
        {
            return CallTool("order_summary", new Dictionary<string, object?>());
        }

        // 3) 指定订单详情
        if (orderId is not null)
        {
            return CallTool("order_get", new Dictionary<string, object?> { ["orderId"] = orderId });
        }

        // 4) 搜索
        if (text.Contains("搜索", StringComparison.Ordinal) ||
            text.Contains("查找", StringComparison.Ordinal) ||
            text.Contains("查询", StringComparison.Ordinal) ||
            text.Contains("列出", StringComparison.Ordinal) ||
            text.Contains("哪些", StringComparison.Ordinal) ||
            text.Contains("看看", StringComparison.Ordinal))
        {
            string keyword = ExtractKeyword(text);
            var arguments = new Dictionary<string, object?>();
            if (keyword.Length > 0)
            {
                arguments["keyword"] = keyword;
            }

            return CallTool("order_search", arguments);
        }

        // 5) 默认：列出全部订单
        return CallTool("order_search", new Dictionary<string, object?>());
    }

    private static ChatMessage CallTool(string toolName, Dictionary<string, object?> arguments)
        => new(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent(Guid.NewGuid().ToString("N"), toolName, arguments),
        });

    private static ChatMessage Text(string text)
        => new(ChatRole.Assistant, $"{OfflineHint}\n{text}");

    private static string? ExtractOrderId(string text)
    {
        int index = text.IndexOf("SO-", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        string candidate = text.Substring(index);
        int length = 0;
        while (length < candidate.Length && (char.IsLetterOrDigit(candidate[length]) || candidate[length] == '-'))
        {
            length++;
        }

        string id = candidate.Substring(0, length);
        return id.Length > 3 ? id : null;
    }

    /// <summary>从文本里提取第一个金额（支持小数与千分位逗号）。</summary>
    private static decimal? ExtractAmount(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d[\d,]*(?:\.\d+)?)");
        if (!match.Success)
        {
            return null;
        }

        string raw = match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(raw, out decimal amount) && amount > 0 ? amount : null;
    }

    /// <summary>从文本里猜客户名：优先「客户/给/为」后面的片段，否则用「新客户」。</summary>
    private static string ExtractCustomerName(string text)
    {
        foreach (string marker in new[] { "客户", "给", "为" })
        {
            int index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            string tail = text.Substring(index + marker.Length).Trim(' ', '，', '。', ':', '：');
            int cut = tail.IndexOfAny([' ', '，', '。', ',', '金', '额']);
            if (cut > 0)
            {
                tail = tail.Substring(0, cut);
            }

            if (tail.Length > 0)
            {
                return tail;
            }
        }

        return "AI 新增客户";
    }

    private static string ExtractKeyword(string text)
    {
        string[] stopWords = ["搜索", "查找", "查询", "列出", "看看", "哪些", "订单", "所有", "全部", "的", "一下", "帮我", "请"];
        string keyword = text;
        foreach (string word in stopWords)
        {
            keyword = keyword.Replace(word, " ", StringComparison.Ordinal);
        }

        return keyword.Trim();
    }

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
}

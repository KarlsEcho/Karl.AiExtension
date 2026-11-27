using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace AI.Extension.Mcp.StdioBridge;

/// <summary>
/// 把标准输入/输出（MCP stdio）透明转发到 WPF 进程内的命名管道。
/// 不解析 JSON，也不依赖 MCP SDK，只做双向字节搬运。
/// </summary>
internal static class Program
{
    private const int DefaultConnectTimeoutSeconds = 15;

    private static async Task<int> Main(string[] args)
    {
        string? pipeName = null;
        int timeoutSeconds = DefaultConnectTimeoutSeconds;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out int parsed):
                    timeoutSeconds = Math.Max(1, parsed);
                    i++;
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            Console.Error.WriteLine("[AiMcpStdioBridge] 缺少 --pipe <管道名>。");
            PrintUsage();
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(timeoutSeconds * 1000, cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[AiMcpStdioBridge] 连接管道 '{pipeName}' 失败：{exception.Message}");
            return 3;
        }

        using Stream stdin = Console.OpenStandardInput();
        using Stream stdout = Console.OpenStandardOutput();

        Task toPipe = CopyAsync(stdin, pipe, cancellation.Token);
        Task fromPipe = CopyAsync(pipe, stdout, cancellation.Token);

        await Task.WhenAny(toPipe, fromPipe).ConfigureAwait(false);
        cancellation.Cancel();

        try
        {
            await Task.WhenAll(toPipe, fromPipe).ConfigureAwait(false);
        }
        catch
        {
            // 任一侧断开都是正常退出路径
        }

        return 0;
    }

    private static async Task CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            int read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return;
            }

            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("用法: AiMcpStdioBridge.exe --pipe <WPF 命名管道名> [--timeout 秒]");
        Console.Error.WriteLine("示例: AiMcpStdioBridge.exe --pipe Karl.AiExtension.Demo.Mcp");
    }
}

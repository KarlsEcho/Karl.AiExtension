using Microsoft.Extensions.Logging;

namespace AI.Extension.Mcp;

/// <summary>
/// 安全日志工厂：包装宿主提供的 <see cref="ILoggerFactory"/>，吞掉日志提供程序的写入异常。
/// <para>
/// 某些终端环境里 Windows EventLog 源不可写（没有注册表/事件日志权限），
/// 日志提供程序抛出的 <see cref="AggregateException"/> 会被 MCP SDK 的内部日志路径带出来，
/// 表现为工具调用/连接失败。日志是诊断设施，不应该影响业务，所以统一在这里兜底。
/// </para>
/// </summary>
internal sealed class SafeLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory _inner;

    public SafeLoggerFactory(ILoggerFactory inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public ILogger CreateLogger(string categoryName) => new SafeLogger(_inner.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => _inner.AddProvider(provider);

    public void Dispose() => _inner.Dispose();

    private sealed class SafeLogger : ILogger
    {
        private readonly ILogger _inner;

        public SafeLogger(ILogger inner)
        {
            _inner = inner;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            try
            {
                return _inner.BeginScope(state);
            }
            catch
            {
                return null;
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return _inner.IsEnabled(logLevel);
            }
            catch
            {
                return false;
            }
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
            catch
            {
                // 日志提供程序不可用不应影响业务
            }
        }
    }
}

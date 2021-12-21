using Microsoft.Extensions.Logging;

namespace Obsidian.Logging;

public sealed class LoggerProvider : ILoggerProvider
{
    private ConcurrentDictionary<string, ILogger> loggers = new();

    public delegate ILogger LoggerFactoryMethod(string category, LogLevel logLevel);
    private readonly LoggerFactoryMethod loggerFactory;

    private LogLevel MinimumLevel { get; }

    private bool disposed = false;

    public LoggerProvider(LoggerFactoryMethod loggerFactory, LogLevel minLevel = LogLevel.Information)
    {
        MinimumLevel = minLevel;
        this.loggerFactory = loggerFactory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (disposed)
            throw new ObjectDisposedException("This logger provider is already disposed.");

        return loggers.GetOrAdd(categoryName, loggerFactory(categoryName, MinimumLevel));
    }

    public void Dispose()
    {
        loggers.Clear();
        loggers = null!;
        disposed = true;
    }
}

using Microsoft.Extensions.Logging;

namespace Obsidian.Hosting.Logging;
public sealed class LoggerProvider : ILoggerProvider
{
    private ConcurrentDictionary<string, Logger>? loggers = new();

    private LogLevel MinimumLevel { get; }

    private bool disposed = false;

    internal LoggerProvider(LogLevel minLevel = LogLevel.Information)
    {
        MinimumLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        return loggers!.GetOrAdd(categoryName, name => new Logger(name, MinimumLevel));
    }

    public void Dispose()
    {
        loggers = null;
        disposed = true;
    }
}

using Microsoft.Extensions.Logging;
<<<<<<<< HEAD:Obsidian/Hosting/Logging/LoggerProvider.cs

namespace Obsidian.Hosting.Logging;
public sealed class LoggerProvider : ILoggerProvider
========
using System.Collections.Concurrent;

namespace Obsidian.API.Logging;

public class LoggerProvider : ILoggerProvider
>>>>>>>> master:Obsidian.API/Logging/LoggerProvider.cs
{
    private ConcurrentDictionary<string, Logger>? loggers = new();

    private LogLevel MinimumLevel { get; }

    private bool disposed = false;

    public LoggerProvider(LogLevel minLevel = LogLevel.Information)
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

using Microsoft.Extensions.Logging;

namespace Obsidian.Logging;

internal sealed class LoggerDummy : ILogger<Server>, IDisposable
{
    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }


    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }

    public void Dispose()
    {
    }
}

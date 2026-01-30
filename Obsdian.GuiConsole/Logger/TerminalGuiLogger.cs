using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Window;
using Terminal.Gui.Views;

namespace Obsidian.GuiConsole.Logger;

public class TerminalGuiLogger : ILogger
{
    private readonly ObsdianConsole _console;
    private readonly LogLevel _minLevel;

    public TerminalGuiLogger(ObsdianConsole console, LogLevel minLevel)
    {
        _console = console;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        
        _console.AppendLog(logLevel,message);
    }
}

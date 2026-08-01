using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Window;
using Terminal.Gui.Views;

namespace Obsidian.GuiConsole.Logger;

/// <summary>
/// Logger implementation that logs messages to the Terminal GUI Console
/// </summary>
public class TerminalGuiLogger : ILogger
{
    private readonly ObsidianConsole _console;
    private readonly LogLevel _minLevel;

    public TerminalGuiLogger(ObsidianConsole console, LogLevel minLevel)
    {
        _console = console;
        _minLevel = minLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;
    /// <summary>
    /// Log message to the GUI Console
    /// </summary>
    /// <param name="logLevel">level</param>
    /// <param name="eventId">evt</param>
    /// <param name="state">state</param>
    /// <param name="exception">error</param>
    /// <param name="formatter">format</param>
    /// <typeparam name="TState">type of state</typeparam>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        
        _console.AppendLog(logLevel,message);
    }
}

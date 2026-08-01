using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Window;

namespace Obsidian.GuiConsole.Logger;

public class TerminalGuiLoggerProvider : ILoggerProvider
{
    private readonly ObsidianConsole _console;
    private readonly LogLevel _minLevel;

    public TerminalGuiLoggerProvider(ObsidianConsole console, LogLevel minLevel)
    {
        _console = console;
        _minLevel = minLevel;
    }

    /// <summary>
    /// Create a logger for the specified category
    /// </summary>
    /// <param name="categoryName"></param>
    /// <returns></returns>
    public ILogger CreateLogger(string categoryName) => new TerminalGuiLogger(_console, _minLevel);

    public void Dispose() { }
}

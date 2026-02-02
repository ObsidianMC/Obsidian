using Microsoft.Extensions.Logging;
using Obsidian.GuiConsole.Window;
using Terminal.Gui.Views;

namespace Obsidian.GuiConsole.Logger;

public class TerminalGuiLoggerProvider: ILoggerProvider
{
    private readonly ObsidianConsole _console;
    private readonly LogLevel _minLevel;

    public TerminalGuiLoggerProvider(ObsidianConsole console, LogLevel minLevel)
    {
        _console = console;
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new TerminalGuiLogger(_console, _minLevel);
    }

    public void Dispose() { }
}

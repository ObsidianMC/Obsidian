namespace Obsidian.Console;

/// <summary>The terminal operations used by the command line and log writer.</summary>
public interface IConsoleDevice
{
    public bool IsInteractive { get; }

    public bool IsErrorRedirected { get; }

    public int Width { get; }

    public bool KeyAvailable { get; }

    public ConsoleKeyInfo ReadKey();

    public void Write(string text, bool standardError = false);
}

namespace Obsidian.Console;

/// <summary>Options for commands read from standard input.</summary>
public sealed class ConsoleCommandOptions
{
    /// <summary>Prefix for the interactive prompt and submitted command log entries.</summary>
    public string Prompt { get; set; } = "> ";

    /// <summary>Record submitted commands in the log, including commands from redirected input.</summary>
    public bool EchoCommands { get; set; } = true;

    /// <summary>Use the interactive prompt, with line editing and completion, when the terminal supports it.</summary>
    public bool Interactive { get; set; } = true;

    /// <summary>Stop the server when standard input ends, e.g. when the process that started it went away.</summary>
    public bool StopOnEndOfInput { get; set; }
}

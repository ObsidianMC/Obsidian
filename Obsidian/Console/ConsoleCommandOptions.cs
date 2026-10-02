namespace Obsidian.Console;

/// <summary>Options for commands read from standard input.</summary>
public sealed class ConsoleCommandOptions
{
    /// <summary>Prefix for the interactive prompt and submitted command log entries.</summary>
    public string Prompt { get; set; } = "> ";

    /// <summary>Record submitted commands in the log, including commands from redirected input.</summary>
    public bool EchoCommands { get; set; } = true;
}

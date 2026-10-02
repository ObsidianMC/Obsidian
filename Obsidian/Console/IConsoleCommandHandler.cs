using System.Threading;

namespace Obsidian.Console;

/// <summary>
/// Handles command lines typed into the server console. Register implementations in DI; they are offered each
/// command in registration order until one reports that it handled it.
/// </summary>
public interface IConsoleCommandHandler
{
    /// <summary>Attempts to handle <paramref name="commandLine"/>.</summary>
    /// <returns><see langword="true"/> if the command was recognised and handled.</returns>
    public ValueTask<bool> HandleAsync(string commandLine, CancellationToken cancellationToken);

    /// <summary>Completes the word containing the caret without running a command.</summary>
    public ValueTask<ConsoleCompletion?> CompleteAsync(string commandLine, int cursor, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ConsoleCompletion?>(null);
}

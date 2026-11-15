namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Signals that no command matches the name that was typed.
/// </summary>
public class CommandNotFoundException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CommandNotFoundException(string message) : base(message)
    {

    }
}

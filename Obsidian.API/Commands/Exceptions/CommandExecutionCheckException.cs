namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Thrown when the sender fails one of a command's execution checks. The command handler catches it and tells the
/// sender.
/// </summary>
public class CommandExecutionCheckException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandExecutionCheckException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CommandExecutionCheckException(string message) : base(message)
    {

    }
}

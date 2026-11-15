namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Signals that a command was given a context of the wrong type.
/// </summary>
public class InvalidCommandContextTypeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidCommandContextTypeException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidCommandContextTypeException(string message) : base(message)
    {

    }
}

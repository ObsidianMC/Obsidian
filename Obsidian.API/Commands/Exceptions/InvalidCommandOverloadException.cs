namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Signals that no overload of a command accepts the arguments it was given.
/// </summary>
public class InvalidCommandOverloadException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidCommandOverloadException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InvalidCommandOverloadException(string message) : base(message)
    {

    }
}

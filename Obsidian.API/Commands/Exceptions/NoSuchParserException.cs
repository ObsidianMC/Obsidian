namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Thrown when a command parameter's type has no registered argument parser.
/// </summary>
public class NoSuchParserException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NoSuchParserException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public NoSuchParserException(string message) : base(message)
    {

    }
}

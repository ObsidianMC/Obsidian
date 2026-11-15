namespace Obsidian.API.Commands.Exceptions;

/// <summary>
/// Thrown when a command's argument can't be parsed to its parameter's type.
/// </summary>
public class CommandArgumentParsingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandArgumentParsingException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CommandArgumentParsingException(string message) : base(message)
    {

    }
}

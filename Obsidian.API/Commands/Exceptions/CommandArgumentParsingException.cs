namespace Obsidian.API.Commands.Exceptions;

public class CommandArgumentParsingException : Exception
{
    public CommandArgumentParsingException(string message) : base(message)
    {

    }
}

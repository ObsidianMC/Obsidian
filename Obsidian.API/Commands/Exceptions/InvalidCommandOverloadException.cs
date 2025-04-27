namespace Obsidian.API.Commands.Exceptions;

public class InvalidCommandOverloadException : Exception
{
    public InvalidCommandOverloadException(string message) : base(message)
    {

    }
}

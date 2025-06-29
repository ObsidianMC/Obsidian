namespace Obsidian.API.Commands.Exceptions;

public class CommandNotFoundException : Exception
{
    public CommandNotFoundException(string message) : base(message)
    {

    }
}

namespace Obsidian.Commands.Framework.Exceptions;

public class CommandNotFoundException : Exception
{
    public CommandNotFoundException(string message) : base(message)
    {

    }
}

using System;

namespace Obsidian.Commands.Framework.Exceptions;

public class CommandExecutionCheckException : Exception
{
    public CommandExecutionCheckException(string message) : base(message)
    {

    }
}

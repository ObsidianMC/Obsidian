using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandExecutionCheckException : Exception
    {
        public CommandExecutionCheckException(string message) : base(message)
        {

        }
    }
}

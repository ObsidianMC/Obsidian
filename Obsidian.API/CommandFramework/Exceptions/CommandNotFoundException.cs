using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandNotFoundException : Exception
    {
        public CommandNotFoundException(string message) : base(message)
        {

        }
    }
}

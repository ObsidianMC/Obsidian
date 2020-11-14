using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandArgumentParsingException : Exception
    {
        public CommandArgumentParsingException(string message) : base(message)
        {

        }
    }
}

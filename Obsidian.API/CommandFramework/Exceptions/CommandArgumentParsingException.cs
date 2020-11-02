using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandArgumentParsingException : Exception
    {
        public CommandArgumentParsingException(string message) : base(message)
        {

        }
    }
}

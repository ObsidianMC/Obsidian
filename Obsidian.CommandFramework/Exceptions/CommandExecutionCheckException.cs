using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandExecutionCheckException : Exception
    {
        public CommandExecutionCheckException(string message) : base(message)
        {

        }
    }
}

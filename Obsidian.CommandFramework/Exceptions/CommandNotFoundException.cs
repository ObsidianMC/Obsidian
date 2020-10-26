using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class CommandNotFoundException : Exception
    {
        public CommandNotFoundException(string message) : base(message)
        {

        }
    }
}

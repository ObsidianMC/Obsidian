using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class InvalidCommandOverloadException : Exception
    {
        public InvalidCommandOverloadException(string message) : base(message)
        {

        }
    }
}

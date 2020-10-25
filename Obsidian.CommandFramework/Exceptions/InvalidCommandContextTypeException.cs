using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class InvalidCommandContextTypeException : Exception
    {
        public InvalidCommandContextTypeException(string message) : base(message)
        {

        }
    }
}

using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class InvalidCommandContextTypeException : Exception
    {
        public InvalidCommandContextTypeException(string message) : base(message)
        {

        }
    }
}

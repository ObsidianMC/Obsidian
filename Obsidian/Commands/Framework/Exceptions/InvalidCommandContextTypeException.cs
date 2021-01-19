using System;

namespace Obsidian.Commands.Framework.Exceptions
{
    public class InvalidCommandContextTypeException : Exception
    {
        public InvalidCommandContextTypeException(string message) : base(message)
        {

        }
    }
}

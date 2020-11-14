using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class InvalidCommandOverloadException : Exception
    {
        public InvalidCommandOverloadException(string message) : base(message)
        {

        }
    }
}

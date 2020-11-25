using System;

namespace Obsidian.CommandFramework.Exceptions
{
    public class NoSuchParserException : Exception
    {
        public NoSuchParserException(string message) : base(message)
        {

        }
    }
}

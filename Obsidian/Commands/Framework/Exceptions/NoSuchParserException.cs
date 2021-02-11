using System;

namespace Obsidian.Commands.Framework.Exceptions
{
    public class NoSuchParserException : Exception
    {
        public NoSuchParserException(string message) : base(message)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class NoSuchParserException : Exception
    {
        public NoSuchParserException(string message) : base(message)
        {

        }
    }
}

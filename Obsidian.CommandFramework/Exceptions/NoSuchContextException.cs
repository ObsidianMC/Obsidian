using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Exceptions
{
    public class NoSuchContextException : Exception
    {
        public NoSuchContextException(string msg) : base(msg) { }
    }
}

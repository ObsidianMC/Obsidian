using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class CommandGroupAttribute : Attribute
    {
        public CommandGroupAttribute(string groupname, params string[] aliases)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute
    {
        public string CommandName;
        public string[] Aliases;

        public CommandAttribute(string commandname, params string[] aliases)
        {
            this.CommandName = commandname;
            this.Aliases = aliases;
        }
    }
}

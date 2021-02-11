using System;

namespace Obsidian.API
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

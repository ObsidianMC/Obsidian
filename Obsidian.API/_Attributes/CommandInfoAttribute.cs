using System;

namespace Obsidian.API
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class CommandInfoAttribute : Attribute
    {
        public string Description;
        public string Usage;

        public CommandInfoAttribute(string description = "", string usage = "")
        {
            this.Description = description;
            this.Usage = usage;
        }
    }
}

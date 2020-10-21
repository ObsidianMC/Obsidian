using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Obsidian.CommandFramework.Entities
{
    public struct CommandInfo
    {
        public string CommandName;
        public string Description;
        public CommandParam[] Parameters;

        public CommandInfo(string name, string desc, CommandParam[] parameters)
        {
            CommandName = name;
            Description = desc;
            Parameters = parameters;
        }
    }

    public struct CommandParam
    {
        public string Name;
        public Type Type;
        public bool Remainder;

        public CommandParam(string name, Type type)
        {
            this.Name = name; 
            this.Type = type;
            this.Remainder = false; // TODO support for remainders
        }
    }
}

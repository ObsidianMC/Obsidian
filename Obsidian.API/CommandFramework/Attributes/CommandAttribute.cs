using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandAttribute : Attribute
    {
        public CommandAttribute(string commandname, params string[] aliases)
        {

        }
    }
}

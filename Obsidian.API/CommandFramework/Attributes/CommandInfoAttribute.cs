using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.CommandFramework.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class CommandInfoAttribute : Attribute
    {
        public CommandInfoAttribute(string description = "", string usage = "")
        {

        }
    }
}

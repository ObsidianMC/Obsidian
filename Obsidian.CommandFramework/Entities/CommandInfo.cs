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
        public bool HasSubcommands;
        public bool RequiresOp;
    }
}

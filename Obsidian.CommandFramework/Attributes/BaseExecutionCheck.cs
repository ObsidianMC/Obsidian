using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework.Attributes
{
    public abstract class BaseExecutionCheckAttribute : Attribute
    {
        public abstract Task<bool> RunChecksAsync(BaseCommandContext ctx);
    }
}

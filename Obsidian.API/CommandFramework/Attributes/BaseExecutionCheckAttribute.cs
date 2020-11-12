using Obsidian.CommandFramework.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework.Attributes
{
    public abstract class BaseExecutionCheckAttribute : Attribute
    {
        public virtual async Task<bool> RunChecksAsync(ObsidianContext ctx)
        {
            throw new Exception($"RunChecksAsync was not implemented for {this.GetType().Name}!");
        }
    }
}

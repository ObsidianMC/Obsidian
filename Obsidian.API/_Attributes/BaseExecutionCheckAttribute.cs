using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public abstract class BaseExecutionCheckAttribute : Attribute
    {
        public virtual Task<bool> RunChecksAsync(CommandContext ctx)
        {
            return Task.FromException<bool>(new Exception($"RunChecksAsync was not implemented for {GetType().Name}!"));
        }
    }
}

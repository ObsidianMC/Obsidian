using System;
using System.Threading.Tasks;

namespace Obsidian.CommandFramework.Attributes
{
    public abstract class BaseExecutionCheckAttribute : Attribute
    {
        public virtual Task<bool> RunChecksAsync(ObsidianContext ctx)
        {
            return Task.FromException<bool>(new Exception($"RunChecksAsync was not implemented for {GetType().Name}!"));
        }
    }
}

using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public abstract class BaseExecutionCheckAttribute : Attribute
    {
        public abstract Task<bool> RunChecksAsync(CommandContext context);
    }
}

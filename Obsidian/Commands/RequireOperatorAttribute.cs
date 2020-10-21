using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequireOperatorAttribute : BaseExecutionCheckAttribute
    {
        public override async Task<bool> RunChecksAsync(BaseCommandContext ctx)
        {
            if (ctx is ObsidianContext c)
            {
                var player = c.Player;

                return c.Server.Operators.IsOperator(player);
            }

            throw new Exception($"This context ({ctx.ToString()}) is unsupported");
        }
    }
}
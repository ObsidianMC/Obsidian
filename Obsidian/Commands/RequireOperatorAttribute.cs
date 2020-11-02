using Obsidian.API;
using Obsidian.CommandFramework.Attributes;
using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
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
                var player = (Player)c.Player;
                var server = (Server)c.Server;

                return server.Operators.IsOperator(player);
            }

            throw new Exception($"This context ({ctx.ToString()}) is unsupported");
        }
    }
}
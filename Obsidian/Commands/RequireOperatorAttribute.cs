using Qmmands;

using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequireOperatorAttribute : CheckBaseAttribute
    {
        public override Task<CheckResult> CheckAsync(ICommandContext context, IServiceProvider provider)
        {
            if (context is CommandContext c)
            {
                var player = c.Player;

                if (c.Server.Operators.IsOperator(player))
                {
                    return Task.FromResult(CheckResult.Successful);
                }

                return Task.FromResult(CheckResult.Unsuccessful($"Player ({player.Username}) is not in operators list."));
            }

            throw new Exception($"This context ({context.ToString()}) is unsupported");
        }
    }
}
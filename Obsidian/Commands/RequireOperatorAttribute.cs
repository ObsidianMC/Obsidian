using Qmmands;

using System;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    public class RequireOperatorAttribute : CheckAttribute
    {

        public override ValueTask<CheckResult> CheckAsync(CommandContext context)
        {
            if (context is ObsidianContext c)
            {
                var player = c.Player;

                if (c.Server.Operators.IsOperator(player))
                {
                    return new ValueTask<CheckResult>(CheckResult.Successful);
                }

                return new ValueTask<CheckResult>(CheckResult.Unsuccessful($"Player ({player.Username}) is not in operators list."));
            }

            throw new Exception($"This context ({context.ToString()}) is unsupported");
        }
    }
}
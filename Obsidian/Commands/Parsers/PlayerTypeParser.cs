using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.CommandFramework.Entities;
using Obsidian.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class PlayerTypeParser : BaseArgumentParser<Player>
    {
        public override bool TryParseArgument(string input, BaseCommandContext context, out Player result)
        {
            var ctx = (ObsidianContext)context;

            Player player = null;

            if (ctx.Server.OnlinePlayers.TryGetValue(Guid.Parse(input), out Player pl) || ctx.Server.OnlinePlayers.Any(x => x.Value.Username == input))
                player = pl ?? ctx.Server.OnlinePlayers.FirstOrDefault(x => x.Value.Username == input).Value;

            result = player;
            return true;
        }
    }
}

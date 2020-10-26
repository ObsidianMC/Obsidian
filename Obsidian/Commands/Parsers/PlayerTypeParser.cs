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

            if(Guid.TryParse(input, out Guid guid))
            {
                // is valid GUID, try find with guid
                ctx.Server.OnlinePlayers.TryGetValue(guid, out player);
            }
            else
            {
                // is not valid guid, try find with name
                player = ctx.Server.OnlinePlayers.FirstOrDefault(x => x.Value.Username == input).Value;
            }

            result = player;
            return true;
        }
    }
}

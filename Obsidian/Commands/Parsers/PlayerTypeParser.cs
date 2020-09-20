using Obsidian.Entities;
using Qmmands;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class PlayerTypeParser : TypeParser<Player>
    {
        public override ValueTask<TypeParserResult<Player>> ParseAsync(Parameter parameter, string value, CommandContext context)
        {
            var ctx = (ObsidianContext)context;

            Player player = null;

            if (ctx.Server.OnlinePlayers.TryGetValue(Guid.Parse(value), out Player pl) || ctx.Server.OnlinePlayers.Any(x => x.Value.Username == value))
                player = pl ?? ctx.Server.OnlinePlayers.FirstOrDefault(x => x.Value.Username == value).Value;

            return new ValueTask<TypeParserResult<Player>>(TypeParserResult<Player>.Successful(player));
        }
    }
}

using Obsidian.API;
using Obsidian.CommandFramework;
using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.Entities;
using System;
using System.Linq;

namespace Obsidian.Commands.Parsers
{
    public class PlayerTypeParser : BaseArgumentParser<IPlayer>
    {
        public PlayerTypeParser() : base("obsidian:player") { }
        public override bool TryParseArgument(string input, ObsidianContext context, out IPlayer result)
        {
            var ctx = context;
            var server = (Server)ctx.Server;

            Player player = null;

            if (Guid.TryParse(input, out Guid guid))
            {
                // is valid GUID, try find with guid
                server.OnlinePlayers.TryGetValue(guid, out player);
            }
            else
            {
                // is not valid guid, try find with name
                player = server.OnlinePlayers.FirstOrDefault(x => x.Value.Username == input).Value;
            }

            result = player;
            return true;
        }
    }
}

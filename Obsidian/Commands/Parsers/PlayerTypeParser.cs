using Obsidian.Entities;

namespace Obsidian.Commands.Parsers;

public class PlayerTypeParser : BaseArgumentParser<IPlayer>
{
    public PlayerTypeParser() : base(48, "obsidian:player")
    {
    }

    public override bool TryParseArgument(string input, CommandContext context, out IPlayer result)
    {
        var server = (Server)context.Server;

        Player player = null;

        if (Guid.TryParse(input, out Guid guid))
        {
            // is valid GUID, try find with guid
            server.OnlinePlayers.TryGetValue(guid, out player);
        }
        else
        {
            // is not valid GUID, try find with name
            player = server.OnlinePlayers.FirstOrDefault(x => x.Value.Username == input).Value;
        }

        result = player;
        return true;
    }
}

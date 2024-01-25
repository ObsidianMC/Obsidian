namespace Obsidian.API.Commands.ArgumentParsers;

public class PlayerArgumentParser : BaseArgumentParser<IPlayer>
{
    public PlayerArgumentParser() : base(7, "minecraft:game_profile")
    {
    }

    //TODO support selectors
    public override bool TryParseArgument(string input, CommandContext context, out IPlayer result)
    {
        var server = context.Server;
        IPlayer? player = null;

        if (Guid.TryParse(input, out var guid))
            // is valid GUID, try find with guid
            player = server.GetPlayer(guid);
        else
            // is not valid GUID, try find with name
            player = server.GetPlayer(input);

        result = player;
        return true;
    }
}

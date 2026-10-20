namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads an online player by name or UUID, or the first player a target selector like <c>@p</c> selects.
/// </summary>
/// <remarks>Parses to <see langword="null"/> when no such player is online.</remarks>
[ArgumentParser("minecraft:game_profile")]
public sealed partial class PlayerArgumentParser : BaseArgumentParser<IPlayer>
{
    public override bool TryParseArgument(string input, CommandContext context, out IPlayer result)
    {
        var server = context.Server;

        if (input.StartsWith('@'))
        {
            result = null!;

            if (!EntitySelector.TryParse(input, out var selector))
                return false;

            result = selector.SelectPlayers(context).FirstOrDefault()!;
            return true;
        }

        result = (Guid.TryParse(input, out var guid) ? server.GetPlayer(guid) : server.GetPlayer(input))!;
        return true;
    }
}

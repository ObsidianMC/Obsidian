namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads one entity from a target selector, or a player's name or UUID (see <see cref="EntitySelector"/>).
/// </summary>
/// <remarks>Fails unless the selector finds exactly one entity.</remarks>
[ArgumentParser("minecraft:entity")]
public sealed partial class EntityArgumentParser(EntityFilter mask) : BaseArgumentParser<IEntity>
{
    public EntityFilter Mask { get; set; } = mask;

    public EntityArgumentParser() : this(EntityFilter.SingleEntityOrPlayer) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out IEntity result)
    {
        result = null!;

        if (!EntitySelector.TryParse(input, out var selector) || !selector.Single)
            return false;

        if (selector.Select(ctx) is not [var entity])
            return false;

        result = entity;
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)this.Mask);
    }
}

/// <summary>
/// Reads the entities a target selector selects, or a player by name or UUID (see <see cref="EntitySelector"/>).
/// </summary>
/// <remarks>
/// Selecting no entity still parses, so commands can tell the sender, as vanilla's do; only malformed selectors fail.
/// </remarks>
[ArgumentParser("minecraft:entity")]
public sealed partial class EntitiesArgumentParser : BaseArgumentParser<IEntity[]>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out IEntity[] result)
    {
        result = [];

        if (!EntitySelector.TryParse(input, out var selector))
            return false;

        result = [.. selector.Select(ctx)];
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)EntityFilter.Entities);
    }
}

/// <summary>
/// Reads the players a target selector selects, or a player by name or UUID (see <see cref="EntitySelector"/>).
/// </summary>
/// <remarks>Like <see cref="EntitiesArgumentParser"/>, selecting nobody still parses.</remarks>
[ArgumentParser("minecraft:entity")]
public sealed partial class PlayersArgumentParser : BaseArgumentParser<IPlayer[]>
{
    public override bool TryParseArgument(string input, CommandContext ctx, out IPlayer[] result)
    {
        result = [];

        if (!EntitySelector.TryParse(input, out var selector))
            return false;

        result = [.. selector.SelectPlayers(ctx)];
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)EntityFilter.OnlyPlayers);
    }
}

/// <summary>What an <c>entity</c> argument accepts, which the client checks selectors against.</summary>
[Flags]
public enum EntityFilter : sbyte
{
    /// <summary>Any number of entities of any type.</summary>
    Entities = 0x00,

    /// <summary>At most one entity.</summary>
    SingleEntityOrPlayer = 0x01,

    /// <summary>Only players.</summary>
    OnlyPlayers = 0x02
}

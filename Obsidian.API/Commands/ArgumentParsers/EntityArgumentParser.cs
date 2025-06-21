namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("minecraft:entity")]
public sealed partial class EntityArgumentParser(EntityFilter mask) : BaseArgumentParser<IEntity>
{
    public EntityFilter Mask { get; set; } = mask;

    public EntityArgumentParser() : this(EntityFilter.SingleEntityOrPlayer) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out IEntity result) => throw new NotImplementedException();

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)this.Mask);
    }
}

public enum EntityFilter : sbyte
{
    SingleEntityOrPlayer = 0x01,
    OnlyPlayers = 0x02
}


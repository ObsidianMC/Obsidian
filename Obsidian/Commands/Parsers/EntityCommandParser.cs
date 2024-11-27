namespace Obsidian.Commands.Parsers;

public class EntityCommandParser : CommandParser
{
    public EntityCommadBitMask Mask { get; set; } = EntityCommadBitMask.SingleEntityOrPlayer;

    public EntityCommandParser(EntityCommadBitMask mask) : base(6, "minecraft:entity")
    {
        this.Mask = mask;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteByte((sbyte)this.Mask);
    }
}

public enum EntityCommadBitMask : sbyte
{
    SingleEntityOrPlayer = 0x01,
    OnlyPlayers = 0x02
}

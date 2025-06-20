namespace Obsidian.API.Commands.Parsers;

[CommandParser("minecraft:entity")]
public partial class EntityCommandParser(EntityCommadBitMask mask) : CommandParser
{
    public EntityCommadBitMask Mask { get; set; } = mask;

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

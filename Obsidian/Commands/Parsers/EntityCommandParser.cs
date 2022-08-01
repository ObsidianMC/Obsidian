using Obsidian.Net;

namespace Obsidian.Commands.Parsers;

public class EntityCommandParser : CommandParser
{
    public EntityCommadBitMask Mask { get; set; } = EntityCommadBitMask.SingleEntityOrPlayer;

    public EntityCommandParser(EntityCommadBitMask mask) : base(6, "minecraft:entity") =>
        this.Mask = mask;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)this.Mask);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)this.Mask);
    }
}

public enum EntityCommadBitMask : sbyte
{
    SingleEntityOrPlayer = 0x01,
    OnlyPlayers = 0x02
}

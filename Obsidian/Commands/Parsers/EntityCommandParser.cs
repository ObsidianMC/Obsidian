using Obsidian.Net;

namespace Obsidian.Commands.Parsers;

public class EntityCommandParser : CommandParser
{
    public EntityCommadBitMask Mask { get; set; } = EntityCommadBitMask.SingleEntityOrPlayer;

    public EntityCommandParser(EntityCommadBitMask mask) : base("minecraft:entity") =>
        Mask = mask;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteByteAsync((sbyte)Mask);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteByte((sbyte)Mask);
    }
}

public enum EntityCommadBitMask : sbyte
{
    SingleEntityOrPlayer = 0x01,
    OnlyPlayers = 0x02
}

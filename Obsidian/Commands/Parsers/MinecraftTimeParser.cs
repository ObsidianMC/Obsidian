namespace Obsidian.Commands.Parsers;
public sealed class MinecraftTimeParser : CommandParser
{
    public int Min { get; set; } = 0;

    public MinecraftTimeParser() : base(42, "minecraft:time")
    {
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteInt(Min);
    }
}

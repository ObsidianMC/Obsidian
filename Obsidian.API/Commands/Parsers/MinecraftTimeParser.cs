namespace Obsidian.API.Commands.Parsers;

[CommandParser("minecraft:time")]
public sealed partial class MinecraftTimeParser : CommandParser
{
    public int Min { get; set; } = 0;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteInt(Min);
    }
}

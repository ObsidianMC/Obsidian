namespace Obsidian.API;

public class StringArgumentParser : BaseArgumentParser<string>
{
    public StringArgumentParser() : base(5, "brigadier:string") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out string result)
    {
        result = input;
        return true;
    }
}

public class GuidArgumentParser : BaseArgumentParser<Guid>
{
    public GuidArgumentParser() : base(47, "minecraft:uuid") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out Guid result)
    {
        return Guid.TryParse(input, out result);
    }
}

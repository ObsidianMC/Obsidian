namespace Obsidian.API.Commands.ArgumentParsers;

public sealed class StringArgumentParser : BaseArgumentParser<string>
{
    public StringArgumentParser() : base(5, "brigadier:string") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out string result)
    {
        result = input;
        return true;
    }
}

public sealed class GuidArgumentParser : BaseArgumentParser<Guid>
{
    public GuidArgumentParser() : base(49, "minecraft:uuid") { }
    public override bool TryParseArgument(string input, CommandContext ctx, out Guid result)
    {
        return Guid.TryParse(input, out result);
    }
}

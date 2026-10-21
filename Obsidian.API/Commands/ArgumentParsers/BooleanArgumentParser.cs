namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("brigadier:bool")]
public sealed partial class BoolArgumentParser(bool value) : BaseArgumentParser<bool>
{
    public bool Value { get; } = value;

    public BoolArgumentParser() : this(false) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out bool result)
        => bool.TryParse(input, out result);
}

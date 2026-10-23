namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads a position like vanilla's <c>vec3</c> arguments: three coordinates, each absolute, <c>~</c> relative or all
/// <c>^</c> local (see <see cref="CommandCoordinates"/>).
/// </summary>
[ArgumentParser("minecraft:vec3")]
public sealed partial class LocationArgumentParser : BaseArgumentParser<VectorD>
{
    public override int WordCount => 3;

    public override bool TryParseArgument(string input, CommandContext context, out VectorD result) =>
        CommandCoordinates.TryParse(input, context, centerBlocks: true, wholeNumbers: false, out result);
}

/// <summary>
/// Reads a block's position like vanilla's <c>block_pos</c> arguments: three coordinates, each a whole number,
/// <c>~</c> relative or all <c>^</c> local, naming the block they fall in.
/// </summary>
[ArgumentParser("minecraft:block_pos")]
public sealed partial class BlockPositionArgumentParser : BaseArgumentParser<Vector>
{
    public override int WordCount => 3;

    public override bool TryParseArgument(string input, CommandContext context, out Vector result)
    {
        if (!CommandCoordinates.TryParse(input, context, centerBlocks: false, wholeNumbers: true, out var position))
        {
            result = default;
            return false;
        }

        result = new Vector((int)Math.Floor(position.X), (int)Math.Floor(position.Y), (int)Math.Floor(position.Z));
        return true;
    }
}

namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads <c>true</c> or <c>false</c>, ignoring case.
/// </summary>
[ArgumentParser("brigadier:bool")]
public sealed partial class BoolArgumentParser(bool value) : BaseArgumentParser<bool>
{
    /// <summary>
    /// Gets the value passed to the constructor. Parsing doesn't use it.
    /// </summary>
    public bool Value { get; } = value;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoolArgumentParser"/> class.
    /// </summary>
    public BoolArgumentParser() : this(false) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out bool result)
        => bool.TryParse(input, out result);
}

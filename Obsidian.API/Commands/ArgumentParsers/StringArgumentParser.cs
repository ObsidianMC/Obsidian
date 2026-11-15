namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads a string argument. The server passes the argument's words through as they were typed; <see cref="Type"/>
/// tells the client how much of the line to take.
/// </summary>
[ArgumentParser("brigadier:string")]
public sealed partial class StringArgumentParser(StringType type) : BaseArgumentParser<string>
{
    /// <summary>
    /// Gets how the client reads the string.
    /// </summary>
    public StringType Type { get; } = type;

    /// <summary>
    /// Initializes a new instance of the <see cref="StringArgumentParser"/> class for a <see cref="StringType.QuotablePhrase"/>.
    /// </summary>
    public StringArgumentParser() : this(StringType.QuotablePhrase) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out string result)
    {
        result = input;
        return true;
    }

    /// <summary>
    /// Writes the parser's id and <see cref="Type"/>.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteVarInt((int)Type);
    }
}

/// <summary>
/// Reads a UUID like vanilla's <c>uuid</c> arguments.
/// </summary>
[ArgumentParser("minecraft:uuid")]
public sealed partial class GuidArgumentParser(Guid guid) : BaseArgumentParser<Guid>
{
    /// <summary>
    /// Gets the value passed to the constructor. Parsing doesn't use it.
    /// </summary>
    public Guid Guid { get; } = guid;

    /// <summary>
    /// Initializes a new instance of the <see cref="GuidArgumentParser"/> class.
    /// </summary>
    public GuidArgumentParser() : this(Guid.Empty) { }

    /// <inheritdoc />
    public override bool TryParseArgument(string input, CommandContext ctx, out Guid result)
    {
        return Guid.TryParse(input, out result);
    }
}

/// <summary>
/// How the client reads a <c>brigadier:string</c> argument.
/// </summary>
public enum StringType : int
{
    /// <summary>
    /// Reads a single word.
    /// </summary>
    SingleWord = 0,

    /// <summary>
    /// If it starts with a ", keeps reading until another " (allowing escaping with \). Otherwise behaves the same as <see cref="SingleWord"/>.
    /// </summary>
    QuotablePhrase = 1,

    /// <summary>
    /// Reads the rest of the content after the cursor. Quotes will not be removed.
    /// </summary>
    GreedyPhrase = 2
}

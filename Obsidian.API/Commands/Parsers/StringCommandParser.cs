namespace Obsidian.API.Commands.Parsers;

[CommandParser("brigadier:string")]
public sealed partial class StringCommandParser(StringType type) : CommandParser
{
    public StringType Type { get; } = type;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteVarInt((int)Type);
    }
}

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

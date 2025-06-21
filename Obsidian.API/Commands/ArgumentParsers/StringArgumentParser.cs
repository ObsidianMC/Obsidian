namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("brigadier:string")]
public sealed partial class StringArgumentParser(StringType type) : BaseArgumentParser<string>
{
    public StringType Type { get; } = type;

    public StringArgumentParser() : this(StringType.QuotablePhrase) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out string result)
    {
        result = input;
        return true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteVarInt((int)Type);
    }
}

[ArgumentParser("minecraft:uuid")]
public sealed partial class GuidArgumentParser(Guid guid) : BaseArgumentParser<Guid>
{
    public Guid Guid { get; } = guid;

    public GuidArgumentParser() : this(Guid.Empty) { }

    public override bool TryParseArgument(string input, CommandContext ctx, out Guid result)
    {
        return Guid.TryParse(input, out result);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteUuid(this.Guid);
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

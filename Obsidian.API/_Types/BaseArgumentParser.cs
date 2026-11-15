using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

//TODO async TryParseArgument
/// <summary>Parses one kind of command argument and describes it to clients for command suggestions.</summary>
public abstract class BaseArgumentParser
{
    /// <summary>Gets the parser's registry id in the protocol's command argument type list.</summary>
    public abstract int Id { get; }
    /// <summary>Gets the parser's id, such as <c>brigadier:integer</c>.</summary>
    public abstract string Identifier { get; }

    /// <summary>
    /// How many words of a command line the argument takes, like the three of a position. The words are passed to
    /// the parser joined by single spaces.
    /// </summary>
    public virtual int WordCount => 1;

    /// <summary>Writes the parser's id and any properties to a commands packet.</summary>
    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    internal abstract bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result);

    /// <summary>Returns <see cref="Identifier"/>.</summary>
    public override string ToString() => Identifier;
}

/// <summary>
/// A parser with no properties that never parses a value; it only names an argument type for clients.
/// </summary>
/// <param name="id">The parser's registry id.</param>
/// <param name="resourceLocation">The parser's id, such as <c>minecraft:color</c>.</param>
public sealed class EmptyArgumentParser(int id, string resourceLocation) : BaseArgumentParser
{
    /// <inheritdoc/>
    public override int Id { get; } = id;

    /// <inheritdoc/>
    public override string Identifier { get; } = resourceLocation;

    internal override bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result)
    {
        result = default;
        return false;
    }
}

/// <summary>Parses command arguments into values of <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The type of the parsed value.</typeparam>
public abstract class BaseArgumentParser<T> : BaseArgumentParser
{
    /// <summary>Tries to parse an argument.</summary>
    /// <param name="input">The argument's words, joined by single spaces.</param>
    /// <param name="ctx">The context the command runs in.</param>
    /// <param name="result">The parsed value when parsing succeeds.</param>
    /// <returns>Whether <paramref name="input"/> was a valid argument.</returns>
    public abstract bool TryParseArgument(string input, CommandContext ctx, out T? result);

    internal override bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result)
    {
        if (this.TryParseArgument(input, ctx, out T? tResult))
        {
            result = tResult!;
            return true;
        }

        result = null;

        return false;
    }
}

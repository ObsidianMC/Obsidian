using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

//TODO async TryParseArgument
public abstract class BaseArgumentParser
{
    public abstract int Id { get; }
    public abstract string Identifier { get; }

    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    internal abstract bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result);

    public override string ToString() => Identifier;
}

public sealed class EmptyArgumentParser(int id, string resourceLocation) : BaseArgumentParser
{
    public override int Id { get; } = id;

    public override string Identifier { get; } = resourceLocation;

    internal override bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result)
    {
        result = default;
        return false;
    }
}

public abstract class BaseArgumentParser<T> : BaseArgumentParser
{
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

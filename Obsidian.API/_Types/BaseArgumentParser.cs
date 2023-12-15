using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

//TODO custom parsers needs to get assigned their own ids by the server. Out of scope for this pr so I will leave it like this for now
public abstract class BaseArgumentParser
{
    public int Id { get; }
    public string ParserIdentifier => minecraftType;
    private readonly string minecraftType;

    public BaseArgumentParser(int id, string minecraftType)
    {
        if (!MinecraftArgumentTypes.IsValidMcType(minecraftType))
            throw new Exception($"Invalid minecraft type: {minecraftType} in {GetType().Name}");

        this.Id = id;
        this.minecraftType = minecraftType;
    }

    internal abstract bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result);
}

public abstract class BaseArgumentParser<T>(int id, string minecraftType) : BaseArgumentParser(id, minecraftType)
{
    public abstract bool TryParseArgument(string input, CommandContext ctx, out T result);

    internal override bool TryParseArgument(string input, CommandContext ctx, [NotNullWhen(true)] out object? result)
    {
        if (this.TryParseArgument(input, ctx, out T tResult))
        {
            result = tResult!;
            return true;
        }

        result = null;

        return false;
    }
}

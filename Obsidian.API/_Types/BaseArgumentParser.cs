namespace Obsidian.API;

public abstract class BaseArgumentParser
{
    public string ParserIdentifier => minecraftType;
    private readonly string minecraftType;

    public BaseArgumentParser(string minecraftType)
    {
        if (!MinecraftArgumentTypes.IsValidMcType(minecraftType))
            throw new Exception($"Invalid minecraft type: {minecraftType} in {GetType().Name}");

        this.minecraftType = minecraftType;
    }
}

public abstract class BaseArgumentParser<T> : BaseArgumentParser
{
    public BaseArgumentParser(string minecraftType) : base(minecraftType)
    {
    }

    public abstract bool TryParseArgument(string input, CommandContext ctx, out T result);

    public Type GetParserType()
    {
        return typeof(T);
    }
}

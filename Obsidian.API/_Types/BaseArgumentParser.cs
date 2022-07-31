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
}

public abstract class BaseArgumentParser<T> : BaseArgumentParser
{
    public BaseArgumentParser(int id, string minecraftType) : base(id, minecraftType)
    {
    }

    public abstract bool TryParseArgument(string input, CommandContext ctx, out T result);

    public Type GetParserType()
    {
        return typeof(T);
    }
}

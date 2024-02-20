namespace Obsidian.API;
public  interface IBlockStateProvider
{
    public string Type { get; }

    public IBlock Get();

    public SimpleBlockState GetSimple();
}

public sealed class SimpleBlockState
{
    public required string Name { get; init; }

    public Dictionary<string, string> Properties { get; init; } = [];
}

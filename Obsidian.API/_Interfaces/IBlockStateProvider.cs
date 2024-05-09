namespace Obsidian.API;
public  interface IBlockStateProvider : IRegistryResource
{
    public IBlock Get();

    public SimpleBlockState GetSimple();
}

public sealed class SimpleBlockState
{
    public required string Name { get; init; }

    public Dictionary<string, string> Properties { get; init; } = [];

    public static SimpleBlockState Create(string name, Dictionary<string, string> properties) => new()
    {
        Name = name,
        Properties = properties
    };
}

using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API;

/// <summary>
/// Chooses a block state for a position, like vanilla's BlockStateProvider.
/// </summary>
public interface IBlockStateProvider : IRegistryResource
{
    public IBlock GetState(IRandomSource random, Vector position);
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

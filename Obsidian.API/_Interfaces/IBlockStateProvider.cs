using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API;

/// <summary>
/// Chooses a block state for a position, like vanilla's BlockStateProvider.
/// </summary>
public interface IBlockStateProvider : IRegistryResource
{
    /// <summary>
    /// Chooses the block for <paramref name="position"/>.
    /// </summary>
    /// <param name="random">The random source to draw from.</param>
    /// <param name="position">The block position being placed.</param>
    /// <returns>The chosen block state.</returns>
    public IBlock GetState(IRandomSource random, Vector position);
}

/// <summary>
/// A block state named by its block's identifier and property values, as worldgen data files write it.
/// </summary>
public sealed class SimpleBlockState
{
    /// <summary>
    /// Gets the block's identifier, like <c>minecraft:oak_log</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the state's property values by property name. Properties not listed take their default values.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = [];

    /// <summary>
    /// Creates a state of the block <paramref name="name"/> with the given property values.
    /// </summary>
    /// <param name="name">The block's identifier.</param>
    /// <param name="properties">The property values by property name.</param>
    /// <returns>The new state.</returns>
    public static SimpleBlockState Create(string name, Dictionary<string, string> properties) => new()
    {
        Name = name,
        Properties = properties
    };
}

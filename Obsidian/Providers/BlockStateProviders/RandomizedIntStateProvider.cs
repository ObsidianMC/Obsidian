using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Another provider's state with an int property set to a random value (e.g. cave vine <c>age</c>).
/// </summary>
[ConfiguredFeatureProperty("minecraft:randomized_int_state_provider")]
public sealed class RandomizedIntStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:randomized_int_state_provider";

    public required IBlockStateProvider Source { get; init; }

    public required string Property { get; init; }

    public required IIntProvider Values { get; init; }

    public IBlock GetState(IRandomSource random, Vector position)
    {
        var block = this.Source.GetState(random, position);

        // Like vanilla, the value is only sampled when the state has the property.
        return block.HasProperty(this.Property) ? block.WithProperty(this.Property, this.Values.Sample(random)) : block;
    }
}

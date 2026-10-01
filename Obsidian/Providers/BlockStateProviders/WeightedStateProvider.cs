using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// One of several block states, picked by weight.
/// </summary>
[ConfiguredFeatureProperty("minecraft:weighted_state_provider")]
public sealed class WeightedStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:weighted_state_provider";

    public required WeightedEntry<SimpleBlockState>[] Entries { get; init; }

    private WeightedEntry<IBlock>[] Blocks => field ??= Array.ConvertAll(this.Entries, entry =>
        new WeightedEntry<IBlock> { Data = BlocksRegistry.GetFromSimpleState(entry.Data), Weight = entry.Weight });

    public IBlock GetState(IRandomSource random, Vector position) => WeightedEntry<IBlock>.Pick(this.Blocks, random);
}

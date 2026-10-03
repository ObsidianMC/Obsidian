using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// Always the same block state.
/// </summary>
[ConfiguredFeatureProperty("minecraft:simple_state_provider")]
public sealed class SimpleStateProvider : IBlockStateProvider
{
    public string Type { get; init; } = "minecraft:simple_state_provider";

    public required SimpleBlockState State { get; init; }

    private IBlock Block => field ??= BlocksRegistry.GetFromSimpleState(this.State);

    public IBlock GetState(IRandomSource random, Vector position) => this.Block;
}

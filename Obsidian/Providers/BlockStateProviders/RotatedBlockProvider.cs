using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// The block's default state with a random <c>axis</c> (logs, hay bales...).
/// </summary>
[ConfiguredFeatureProperty("minecraft:rotated_block_provider")]
public sealed class RotatedBlockProvider : IBlockStateProvider
{
    private static readonly string[] axes = ["x", "y", "z"];

    public string Type { get; init; } = "minecraft:rotated_block_provider";

    /// <summary>
    /// Only the block type is used; vanilla starts from its default state.
    /// </summary>
    public required SimpleBlockState State { get; init; }

    private IBlock DefaultBlock => field ??= BlockStateProperties.GetState(this.State.Name);

    public IBlock GetState(IRandomSource random, Vector position) =>
        this.DefaultBlock.WithProperty("axis", axes[random.NextInt(3)]);
}

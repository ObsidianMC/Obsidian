using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.BlockPredicates;

/// <summary>
/// Matches when <see cref="State"/> could survive at the position (e.g. a flower on dirt).
/// </summary>
[ConfiguredFeatureProperty("minecraft:would_survive")]
public sealed class WouldSurvivePredicate : IBlockPredicate
{
    public string Type { get; init; } = "minecraft:would_survive";

    public Vector Offset { get; init; } = Vector.Zero;

    public required SimpleBlockState State { get; init; }

    private IBlock Block => field ??= BlocksRegistry.GetFromSimpleState(this.State);

    public bool Test(IWorldGenLevel level, Vector position) => this.Block.CanSurvive(level, position + this.Offset);
}

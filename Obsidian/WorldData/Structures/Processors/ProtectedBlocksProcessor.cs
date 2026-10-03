using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Skips template blocks whose target position holds a protected block (usually <c>#features_cannot_replace</c>).
/// </summary>
[ConfiguredFeatureProperty("minecraft:protected_blocks")]
public sealed class ProtectedBlocksProcessor : StructureProcessor
{
    /// <summary>The block tag that can't be replaced.</summary>
    public required BlockSet Value { get; init; }

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings) =>
        this.Value.Contains(level.GetBlock(current.Position)) ? null : current;
}

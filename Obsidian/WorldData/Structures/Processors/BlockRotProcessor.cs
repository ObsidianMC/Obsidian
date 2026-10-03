using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Randomly drops template blocks, keeping each with probability <see cref="Integrity"/>.
/// </summary>
/// <remarks>Draws one <c>nextFloat</c> per (rottable) block from <see cref="StructurePlaceSettings.GetRandom"/>.</remarks>
[ConfiguredFeatureProperty("minecraft:block_rot")]
public sealed class BlockRotProcessor : StructureProcessor
{
    public required float Integrity { get; init; }

    /// <summary>Blocks that can rot; all blocks when omitted.</summary>
    public BlockSet? RottableBlocks { get; init; }

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        var random = settings.RentRandom(current.Position);
        try
        {
            var canRot = this.RottableBlocks is null || this.RottableBlocks.Contains(original.Block);
            return canRot && !(random.NextFloat() <= this.Integrity) ? null : current;
        }
        finally
        {
            settings.ReturnRandom(random);
        }
    }
}

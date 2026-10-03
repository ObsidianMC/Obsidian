namespace Obsidian.WorldData.Lighting;

/// <summary>
/// Vanilla's <c>ChunkSkyLightSources</c>: the lowest Y of each column that the sky lights at full strength.
/// </summary>
/// <remarks>
/// Every position at or above a column's lowest source has sky light 15. A column's sources stop above the first block,
/// from the top, that sky light can't pass straight down into (see <see cref="IsEdgeOccluded"/>).
/// </remarks>
internal static class SkyLightSources
{
    /// <summary>
    /// The lowest source of a column that is open all the way down: its sources extend below the world.
    /// </summary>
    public const int BelowWorld = int.MinValue;

    /// <summary>
    /// The lowest sky light source of column (<paramref name="x"/>, <paramref name="z"/>), in chunk coordinates.
    /// </summary>
    /// <returns>The source's Y, which may be just above the world, or <see cref="BelowWorld"/>.</returns>
    public static int LowestSourceY(IChunk chunk, int x, int z)
    {
        var sections = chunk.Sections;
        var above = BlocksRegistry.Air;
        for (var sectionIndex = sections.Length - 1; sectionIndex >= 0; sectionIndex--)
        {
            var section = sections[sectionIndex];
            if (section.IsEmpty)
            {
                above = BlocksRegistry.Air;
                continue;
            }

            for (var y = 15; y >= 0; y--)
            {
                var block = section.GetBlock(x, y, z);
                if (IsEdgeOccluded(above, block))
                    return chunk.MinY + (sectionIndex << 4) + y + 1;

                above = block;
            }
        }

        return BelowWorld;
    }

    /// <summary>
    /// Whether full strength sky light stops between <paramref name="above"/> and the block <paramref name="below"/> it:
    /// <paramref name="below"/> absorbs light, or their touching faces close the gap.
    /// </summary>
    public static bool IsEdgeOccluded(IBlock above, IBlock below) =>
        below.LightBlock() != 0 || BlockLight.LightShapesOcclude(above, below, BlockFace.Down);
}

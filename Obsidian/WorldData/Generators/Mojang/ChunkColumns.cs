namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Bounds for scanning a chunk's columns from the top down without reading the sections above its terrain, which only hold
/// air.
/// </summary>
internal static class ChunkColumns
{
    /// <summary>
    /// The highest Y that may hold anything but air: the top of the highest section that isn't empty (empty sections hold
    /// only air), or one below the chunk when every section is empty.
    /// </summary>
    public static int HighestNonAirY(IChunk chunk)
    {
        var sections = chunk.Sections;
        for (var index = sections.Length - 1; index >= 0; index--)
        {
            if (!sections[index].IsEmpty)
                return chunk.MinY + (index << 4) + 15;
        }

        return chunk.MinY - 1;
    }

    /// <summary>
    /// How many layers up from <paramref name="minY"/> (at most <paramref name="height"/>) a top-down scan needs to read to
    /// see the same blocks as a scan of all of them, apart from air.
    /// </summary>
    public static int NonAirHeight(IChunk chunk, int minY, int height) => Math.Clamp(HighestNonAirY(chunk) + 1 - minY, 0, height);
}

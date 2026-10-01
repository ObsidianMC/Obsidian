namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Maintains the heightmaps vanilla keeps while generating: the world generation ones
/// (<see cref="HeightmapType.WorldSurfaceWG"/> and <see cref="HeightmapType.OceanFloorWG"/>) and the final ones.
/// </summary>
internal static class WorldgenHeightmaps
{
    private static readonly HeightmapType[] finalHeightmaps =
        [HeightmapType.WorldSurface, HeightmapType.OceanFloor, HeightmapType.MotionBlocking, HeightmapType.MotionBlockingNoLeaves];

    /// <summary>
    /// Recomputes both heightmaps from the chunk's blocks. Values are the first free Y above the highest
    /// matching block, like vanilla.
    /// </summary>
    /// <remarks>
    /// The ocean floor stops at the first block that blocks motion, so it passes through fluids.
    /// </remarks>
    public static void Update(IChunk chunk, int minY, int height)
    {
        Span<int> worldSurface = stackalloc int[256];
        Span<int> oceanFloor = stackalloc int[256];

        for (var localZ = 0; localZ < 16; localZ++)
        {
            for (var localX = 0; localX < 16; localX++)
            {
                var column = localZ * 16 + localX;
                worldSurface[column] = minY;
                oceanFloor[column] = minY;

                for (var y = minY + height - 1; y >= minY; y--)
                {
                    var block = chunk.GetBlock(localX, y, localZ);
                    if (block.IsAir)
                        continue;

                    if (worldSurface[column] == minY)
                        worldSurface[column] = y + 1;

                    if (block.BlocksMotion())
                    {
                        oceanFloor[column] = y + 1;
                        break;
                    }
                }
            }
        }

        Set(chunk, HeightmapType.WorldSurfaceWG, worldSurface);
        Set(chunk, HeightmapType.OceanFloorWG, oceanFloor);
    }

    /// <summary>
    /// Recomputes the final heightmaps (<see cref="HeightmapType.WorldSurface"/>, <see cref="HeightmapType.OceanFloor"/>,
    /// <see cref="HeightmapType.MotionBlocking"/> and <see cref="HeightmapType.MotionBlockingNoLeaves"/>) the chunk still has.
    /// </summary>
    /// <remarks>
    /// Features track these heights only while decorating, so they're written back once every feature that can reach the
    /// chunk has run.
    /// </remarks>
    public static void UpdateFinal(IChunk chunk, int minY, int height)
    {
        Span<int> heights = stackalloc int[256];

        foreach (var type in finalHeightmaps)
        {
            if (!chunk.Heightmaps.ContainsKey(type))
                continue;

            for (var localZ = 0; localZ < 16; localZ++)
            {
                for (var localX = 0; localX < 16; localX++)
                {
                    var y = minY + height - 1;
                    while (y >= minY && !Matches(type, chunk.GetBlock(localX, y, localZ)))
                        y--;

                    heights[localZ * 16 + localX] = y + 1;
                }
            }

            Set(chunk, type, heights);
        }
    }

    /// <summary>
    /// Whether a block counts for a heightmap, using vanilla's Heightmap.Types predicates.
    /// </summary>
    public static bool Matches(HeightmapType type, IBlock block) => type switch
    {
        HeightmapType.WorldSurface or HeightmapType.WorldSurfaceWG => !block.IsAir,
        HeightmapType.OceanFloor or HeightmapType.OceanFloorWG => block.BlocksMotion(),
        HeightmapType.MotionBlocking => block.BlocksMotion() || block.HasFluid(),
        // Vanilla's LeavesBlock is abstract; the concrete leaves classes all end with its name.
        HeightmapType.MotionBlockingNoLeaves => (block.BlocksMotion() || block.HasFluid())
            && !block.BlockClass().EndsWith("LeavesBlock", StringComparison.Ordinal),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static void Set(IChunk chunk, HeightmapType type, ReadOnlySpan<int> heights)
    {
        if (!chunk.Heightmaps.TryGetValue(type, out var heightmap))
            return;

        for (var column = 0; column < heights.Length; column++)
            heightmap.Set(column % 16, column / 16, heights[column]);
    }
}

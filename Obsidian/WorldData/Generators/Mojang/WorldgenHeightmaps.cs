namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Maintains the world generation heightmaps (<see cref="HeightmapType.WorldSurfaceWG"/> and
/// <see cref="HeightmapType.OceanFloorWG"/>) that vanilla keeps up to date while generating.
/// </summary>
internal static class WorldgenHeightmaps
{
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

    public static void Set(IChunk chunk, HeightmapType type, ReadOnlySpan<int> heights)
    {
        if (!chunk.Heightmaps.TryGetValue(type, out var heightmap))
            return;

        for (var column = 0; column < heights.Length; column++)
            heightmap.Set(column % 16, column / 16, heights[column]);
    }
}

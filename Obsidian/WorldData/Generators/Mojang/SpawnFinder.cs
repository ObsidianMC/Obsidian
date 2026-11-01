namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// The second half of vanilla's initial world spawn search (MinecraftServer.setInitialSpawn): once the climate search
/// (<see cref="Biomes.SpawnSearch"/>) picks the spawn chunk, a spiral over the chunks around it for a block a player
/// can stand on.
/// </summary>
internal static class SpawnFinder
{
    /// <summary>
    /// Vanilla's <c>ChunkGenerator.getSpawnHeight</c>, which noise generators don't override.
    /// </summary>
    public const int DefaultSpawnHeight = 64;

    /// <summary>
    /// Half the side of the square of chunks searched around the climate spawn chunk.
    /// </summary>
    public const int ChunkSearchRadius = 5;

    /// <summary>
    /// The chunk offsets vanilla visits around the climate spawn chunk, spiraling outwards over an 11x11 square.
    /// </summary>
    public static IEnumerable<(int X, int Z)> SpiralOffsets()
    {
        int x = 0, z = 0, stepX = 0, stepZ = -1;
        var side = ChunkSearchRadius * 2 + 1;

        for (var i = 0; i < side * side; i++)
        {
            if (x >= -ChunkSearchRadius && x <= ChunkSearchRadius && z >= -ChunkSearchRadius && z <= ChunkSearchRadius)
                yield return (x, z);

            if (x == z || x < 0 && x == -z || x > 0 && x == 1 - z)
                (stepX, stepZ) = (-stepZ, stepX);

            x += stepX;
            z += stepZ;
        }
    }

    /// <summary>
    /// Vanilla's <c>PlayerSpawnFinder.getSpawnPosInChunk</c>: the first column (X-major) of a fully generated chunk with a
    /// block a player can stand on, as the position above that block.
    /// </summary>
    /// <param name="hasCeiling">Whether the dimension has a ceiling; such dimensions search down from the spawn height.</param>
    public static Vector? FindSpawnInChunk(IChunk chunk, bool hasCeiling, int spawnHeight = DefaultSpawnHeight)
    {
        for (var localX = 0; localX < 16; localX++)
        {
            for (var localZ = 0; localZ < 16; localZ++)
            {
                var y = FindSpawnInColumn(chunk, localX, localZ, hasCeiling, spawnHeight);
                if (y is not null)
                    return new Vector((chunk.X << 4) + localX, y.Value, (chunk.Z << 4) + localZ);
            }
        }

        return null;
    }

    private static int? FindSpawnInColumn(IChunk chunk, int localX, int localZ, bool hasCeiling, int spawnHeight)
    {
        var top = hasCeiling ? spawnHeight : chunk.Heightmaps[HeightmapType.MotionBlocking].GetHeight(localX, localZ);
        if (top < chunk.MinY)
            return null;

        // Skip columns topped by a fluid: the world surface is above the ocean floor but not above the motion blocking top.
        var worldSurface = chunk.Heightmaps[HeightmapType.WorldSurface].GetHeight(localX, localZ);
        if (worldSurface <= top && worldSurface > OceanFloor(chunk, localX, localZ))
            return null;

        for (var y = top + 1; y >= chunk.MinY; y--)
        {
            var block = y < chunk.MinY + chunk.Height ? chunk.GetBlock(localX, y, localZ) : BlocksRegistry.Air;
            if (block.HasFluid())
                break;

            if (block.HasFullTopCollisionFace())
                return y + 1;
        }

        return null;
    }

    /// <summary>
    /// The ocean floor height (first free Y above the highest motion blocking block), which full chunks don't keep.
    /// </summary>
    private static int OceanFloor(IChunk chunk, int localX, int localZ)
    {
        for (var y = chunk.MinY + chunk.Height - 1; y >= chunk.MinY; y--)
        {
            if (chunk.GetBlock(localX, y, localZ).BlocksMotion())
                return y + 1;
        }

        return chunk.MinY;
    }
}

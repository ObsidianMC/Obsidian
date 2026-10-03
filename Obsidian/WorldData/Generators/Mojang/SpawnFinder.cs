namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Vanilla's initial world spawn search (MinecraftServer.setInitialSpawn): a climate search for the spawn chunk, then
/// a spiral over the chunks around it for a block a player can stand on.
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
    /// Vanilla's <c>Climate.findSpawnPosition</c>: the block (with Y 0) whose climate best fits a spawn target, preferring
    /// positions near the origin.
    /// </summary>
    public static (int X, int Z) FindClimateSpawn(IReadOnlyList<ParameterPoint> targets, ClimateSampler sampler)
    {
        if (targets.Count == 0)
            return (0, 0);

        var best = Evaluate(targets, sampler, 0, 0);
        best = RadialSearch(targets, sampler, best, 2048.0f, 512.0f);
        best = RadialSearch(targets, sampler, best, 512.0f, 32.0f);
        return (best.X, best.Z);
    }

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

    private static Candidate RadialSearch(IReadOnlyList<ParameterPoint> targets, ClimateSampler sampler, Candidate best, float maxRadius,
        float step)
    {
        // The float math matches vanilla's SpawnFinder.radialSearch.
        var angle = 0.0f;
        var radius = step;
        var originX = best.X;
        var originZ = best.Z;

        while (radius <= maxRadius)
        {
            var x = originX + (int)(Math.Sin(angle) * radius);
            var z = originZ + (int)(Math.Cos(angle) * radius);
            var candidate = Evaluate(targets, sampler, x, z);
            if (candidate.Fitness < best.Fitness)
                best = candidate;

            angle += step / radius;
            if (angle > Math.PI * 2)
            {
                angle = 0.0f;
                radius += step;
            }
        }

        return best;
    }

    private static Candidate Evaluate(IReadOnlyList<ParameterPoint> targets, ClimateSampler sampler, int x, int z)
    {
        var sample = sampler.Sample(x >> 2, 0, z >> 2);
        var point = sample with { Depth = 0L };
        var fitness = long.MaxValue;

        foreach (var target in targets)
            fitness = Math.Min(fitness, target.Fitness(point));

        return new Candidate(x, z, fitness * 2048L * 2048L + (long)x * x + (long)z * z);
    }

    private readonly record struct Candidate(int X, int Z, long Fitness);
}

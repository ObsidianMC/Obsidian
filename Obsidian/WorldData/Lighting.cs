namespace Obsidian.WorldData;

internal static class Lighting
{
    public static void InitialFillSkyLight(IChunk chunk)
    {
        Dictionary<Vector, int> spreadBlocks = [];

        // From top down, if the chunk section is empty, just fill it all with sky light
        int cs;
        for (cs = 23; cs >= 0; cs--)
        {
            if (!chunk.Sections[cs].IsEmpty)
            {
                break;
            }
            chunk.Sections[cs].FillSkyLight();
        }

        int startY = ((cs - 4) << 4) + 15; // 4 sections are negative

        // Light the remaining sections
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int level = chunk.GetLightLevel(x, startY + 1, z, LightType.Sky);
                for (int y = startY; y >= -64; y--)
                {
                    var scanPos = new Vector(x, y, z);
                    var b = chunk.GetBlock(scanPos);
                    if (IsOpaque(b))
                    {
                        // Found a non-air, so spread from the air above
                        chunk.SetLightLevel(scanPos + Vector.Up, LightType.Sky, 0);
                        spreadBlocks[scanPos + Vector.Up] = level;
                        break;
                    }

                    if (IsSemitransparent(b)) { level--; }
                    chunk.SetLightLevel(scanPos, LightType.Sky, level);
                    if (level == 0) { break; }

                    // On our way down, we also need to check if skylight would propagate
                    // sideways to an air block that's not exposed to the sky
                    // This is evidenced by a bottom side of a block exposed
                    foreach (Vector dir in EdgeSafeCardinalDirections(x, z))
                    {
                        if (HasSurfaceAbove(scanPos + dir, chunk))
                        {
                            spreadBlocks[scanPos + dir] = level - 1;
                        }
                    }
                }
            }
        }

        foreach (var b in spreadBlocks)
        {
            SpreadLight(b.Key, LightType.Sky, b.Value, chunk);
        }
    }

    public static async Task LightFromNeighbors(IChunk chunk, IWorld world)
    {
        foreach (var (dx, dz, dir, edgeX, edgeZ) in FromNeighborOffsets)
        {
            var neighbor = await world.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);
            if (neighbor is null || neighbor.ChunkStatus < ChunkGenStage.light)
                continue;

            PropagateEdgeLightBetweenChunks(
                sourceChunk: neighbor,
                targetChunk: chunk,
                dir: dir,
                sourceEdgeX: edgeX,
                sourceEdgeZ: edgeZ
            );
        }
    }

    public static async Task LightToNeighbors(IChunk chunk, IWorld world)
    {
        foreach (var (dx, dz, dir, sourceEdgeX, sourceEdgeZ) in ToNeighborOffsets)
        {
            var neighbor = await world.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);
            if (neighbor is null || neighbor.ChunkStatus < ChunkGenStage.light)
                continue;

            PropagateEdgeLightBetweenChunks(
                sourceChunk: chunk,
                targetChunk: neighbor,
                dir: dir,
                sourceEdgeX: sourceEdgeX,
                sourceEdgeZ: sourceEdgeZ
            );
        }
    }

    private static void PropagateEdgeLightBetweenChunks(
        IChunk sourceChunk,
        IChunk targetChunk,
        Vector dir,
        int sourceEdgeX,
        int sourceEdgeZ)
    {
        if (sourceEdgeX >= 0)
        {
            for (int z = 0; z < 16; z++)
            {
                for (int y = -64; y < 320; y++)
                {
                    // Skip empty sections
                    var secIndex = (y >> 4) + 4;
                    if (targetChunk.Sections[secIndex].IsEmpty)
                    {
                        y += 15;
                        continue;
                    }

                    int targetX = dir == Vector.West ? 0 : 15;
                    var targetPos = new Vector(targetX, y, z);

                    // Skip opaque blocks
                    if (IsOpaque(targetChunk.GetBlock(targetPos))) { continue; }

                    foreach (var lt in Enum.GetValues<LightType>())
                    {
                        var level = sourceChunk.GetLightLevel(sourceEdgeX, y, z, lt) - 1;

                        if (level > targetChunk.GetLightLevel(targetPos, lt))
                        {
                            SpreadLight(targetPos, lt, level, targetChunk);
                        }
                    }
                }
            }
        }
        else
        {
            for (int x = 0; x < 16; x++)
            {
                for (int y = -64; y < 320; y++)
                {
                    // Skip empty sections
                    var secIndex = (y >> 4) + 4;
                    if (targetChunk.Sections[secIndex].IsEmpty)
                    {
                        y += 15;
                        continue;
                    }

                    int targetZ = dir == Vector.North ? 0 : 15;
                    var targetPos = new Vector(x, y, targetZ);

                    // Skip opaque blocks
                    if (IsOpaque(targetChunk.GetBlock(targetPos))) { continue; }

                    foreach (var lt in Enum.GetValues<LightType>())
                    {
                        var level = sourceChunk.GetLightLevel(x, y, sourceEdgeZ, lt) - 1;

                        if (level > targetChunk.GetLightLevel(targetPos, lt))
                        {
                            SpreadLight(targetPos, lt, level, targetChunk);
                        }
                    }
                }
            }
        }
    }


    private static void SpreadLight(Vector pos, LightType lt, int level, IChunk chunk)
    {
        var b = chunk.GetBlock(pos);

        // Sanity Checks
        if (level < 1) { return; }
        if (IsOpaque(b)) { return; }
        if (level <= chunk.GetLightLevel(pos, lt)) { return; }

        // If this is a semi-transparent block, a level is lost.
        if (IsSemitransparent(b)) { level--; }
        chunk.SetLightLevel(pos, lt, level);

        // Light level is lost for all types at this point except downwards traveling sky light
        if (lt == LightType.Sky)
        {
            SpreadLight(pos + Vector.Down, lt, level, chunk);
        }

        level--;
        if (lt == LightType.Block)
        {
            SpreadLight(pos + Vector.Down, lt, level, chunk);
        }

        SpreadLight(pos + Vector.Up, lt, level, chunk);

        foreach (Vector dir in EdgeSafeCardinalDirections(pos.X, pos.Z))
        {
            SpreadLight(pos + dir, lt, level, chunk);
        }
    }

    private static readonly (int dx, int dz, Vector dir, int edgeX, int edgeZ)[] FromNeighborOffsets =
    [
        (dx: -1, dz: 0, dir: Vector.West, edgeX: 15, edgeZ: -1),
        (dx: 1, dz: 0, dir: Vector.East, edgeX: 0, edgeZ: -1),
        (dx: 0, dz: -1, dir: Vector.North, edgeX: -1, edgeZ: 15),
        (dx: 0, dz: 1, dir: Vector.South, edgeX: -1, edgeZ: 0)
    ];

    private static readonly (int dx, int dz, Vector dir, int sourceEdgeX, int sourceEdgeZ)[] ToNeighborOffsets =
    [
            (dx: -1, dz: 0, dir: Vector.East, sourceEdgeX: 0, sourceEdgeZ: -1),
            (dx: 1, dz: 0, dir: Vector.West, sourceEdgeX: 15, sourceEdgeZ: -1),
            (dx: 0, dz: -1, dir: Vector.South, sourceEdgeX: -1, sourceEdgeZ: 0),
            (dx: 0, dz: 1, dir: Vector.North, sourceEdgeX: -1, sourceEdgeZ: 15)
    ];

    private static bool IsTransparent(IBlock b) => TagsRegistry.Block.Transparent.Entries.Contains(b.RegistryId);

    private static bool IsSemitransparent(IBlock b) => TagsRegistry.Block.Semitransparent.Entries.Contains(b.RegistryId) || b.Is(BlocksRegistry.Water.Material);

    private static bool IsOpaque(IBlock b) => !(IsTransparent(b) || IsSemitransparent(b));

    private static bool HasSurfaceBelow(Vector pos, IChunk chunk) => IsOpaque(chunk.GetBlock(pos + Vector.Down)) && !IsOpaque(chunk.GetBlock(pos));

    private static bool HasSurfaceAbove(Vector pos, IChunk chunk) => IsOpaque(chunk.GetBlock(pos + Vector.Up)) && !IsOpaque(chunk.GetBlock(pos));

    private static IEnumerable<Vector> EdgeSafeCardinalDirections(int x, int z)
    {
        if (x > 0)
            yield return Vector.West;
        if (x < 15)
            yield return Vector.East;
        if (z > 0)
            yield return Vector.North;
        if (z < 15)
            yield return Vector.South;
    }
}

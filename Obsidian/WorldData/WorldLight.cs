namespace Obsidian.WorldData;

internal static class WorldLight
{
    public static void InitialFillSkyLight(IChunk chunk)
    {
        // Start by directly lighting the entire chunk
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int lightLevel = 15;
                int diffuse = 0;
                var surfaceY = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                for (int y = 319; y >= surfaceY; y--)
                {
                    var secIndex = (y >> 4) + 4;
                    if (chunk.Sections[secIndex].IsEmpty)
                    {
                        y -= 15;
                        continue;
                    }

                    IBlock b = chunk.GetBlock(x, y, z);
                    if (TagsRegistry.Block.Semitransparent.Entries.Contains(b.RegistryId) || b.Is(BlocksRegistry.Water.Material)) { diffuse = 1; }
                    else if (!TagsRegistry.Block.Transparent.Entries.Contains(b.RegistryId)) { lightLevel = 0; }

                    lightLevel = Math.Max(0, lightLevel - diffuse);
                    chunk.SetLightLevel(x, y, z, LightType.Sky, lightLevel);
                    if (lightLevel == 0) { break; }
                }
            }
        }
        // Go back over the chunk and spread light.
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                var y = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                var pos = new Vector(x, y, z);
                var level = chunk.GetLightLevel(pos + Vector.Up, LightType.Sky);
                SetLightAndSpread(pos, LightType.Sky, level, chunk, initial: true);
            }
        }
    }

    public static async Task PropagateFromNeighborsAsync(IChunk chunk, IWorld world)
    {
        if (world is null || chunk is null)
            return;

        // Define the four cardinal neighbor chunk positions
        var neighborOffsets = new[]
        {
            (dx: -1, dz: 0, dir: Vector.West, edgeX: 15, edgeZ: -1),  // West neighbor
            (dx: 1, dz: 0, dir: Vector.East, edgeX: 0, edgeZ: -1),    // East neighbor
            (dx: 0, dz: -1, dir: Vector.North, edgeX: -1, edgeZ: 15), // North neighbor
            (dx: 0, dz: 1, dir: Vector.South, edgeX: -1, edgeZ: 0)    // South neighbor
        };

        foreach (var (dx, dz, dir, edgeX, edgeZ) in neighborOffsets)
        {
            // Get the neighboring chunk (don't schedule generation)
            var neighbor = await world.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);

            // Skip if neighbor doesn't exist or hasn't reached the light stage yet
            if (neighbor is null || neighbor.ChunkStatus < ChunkGenStage.light)
                continue;

            // Scan the neighbor's edge for light values and propagate into current chunk
            if (edgeX >= 0) // West or East neighbor (scan along X edge)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int y = -64; y < 320; y++)
                    {
                        var lightLevel = neighbor.GetLightLevel(edgeX, y, z, LightType.Sky);
                        if (lightLevel > 1) // Only propagate if there's meaningful light (accounting for 1 level loss)
                        {
                            // Calculate the position in the current chunk where light will enter
                            int currentX = dir == Vector.West ? 0 : 15;
                            var targetPos = new Vector(currentX, y, z);

                            // Propagate with reduced level (1 level lost crossing chunk boundary)
                            SetLightAndSpread(targetPos, LightType.Sky, lightLevel - 1, chunk);
                        }
                    }
                }
            }
            else // North or South neighbor (scan along Z edge)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = -64; y < 320; y++)
                    {
                        var lightLevel = neighbor.GetLightLevel(x, y, edgeZ, LightType.Sky);
                        if (lightLevel > 1) // Only propagate if there's meaningful light (accounting for 1 level loss)
                        {
                            // Calculate the position in the current chunk where light will enter
                            int currentZ = dir == Vector.North ? 0 : 15;
                            var targetPos = new Vector(x, y, currentZ);

                            // Propagate with reduced level (1 level lost crossing chunk boundary)
                            SetLightAndSpread(targetPos, LightType.Sky, lightLevel - 1, chunk);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// After a chunk has been lit, propagate its edge light to already-generated neighboring chunks.
    /// This ensures that chunks generated before this one receive light from this newly-lit chunk.
    /// </summary>
    public static async Task PropagateToNeighborsAsync(IChunk chunk, IWorld world)
    {
        if (world is null || chunk is null)
            return;

        // Define the four cardinal neighbor chunk positions
        var neighborOffsets = new[]
        {
            (dx: -1, dz: 0, dir: Vector.East, sourceEdgeX: 0, sourceEdgeZ: -1),   // West neighbor receives from our east edge
            (dx: 1, dz: 0, dir: Vector.West, sourceEdgeX: 15, sourceEdgeZ: -1),   // East neighbor receives from our west edge
            (dx: 0, dz: -1, dir: Vector.South, sourceEdgeX: -1, sourceEdgeZ: 0),  // North neighbor receives from our south edge
            (dx: 0, dz: 1, dir: Vector.North, sourceEdgeX: -1, sourceEdgeZ: 15)   // South neighbor receives from our north edge
        };

        foreach (var (dx, dz, dir, sourceEdgeX, sourceEdgeZ) in neighborOffsets)
        {
            // Get the neighboring chunk (don't schedule generation)
            var neighbor = await world.GetChunkAsync(chunk.X + dx, chunk.Z + dz, scheduleGeneration: false);

            // Skip if neighbor doesn't exist or hasn't reached the light stage yet
            if (neighbor is null || neighbor.ChunkStatus < ChunkGenStage.light)
                continue;

            // Scan our edge for light values and propagate into the neighbor chunk
            if (sourceEdgeX >= 0) // We're scanning along X edge (West or East neighbor)
            {
                for (int z = 0; z < 16; z++)
                {
                    for (int y = -64; y < 320; y++)
                    {
                        var lightLevel = chunk.GetLightLevel(sourceEdgeX, y, z, LightType.Sky);
                        if (lightLevel > 1) // Only propagate if there's meaningful light (accounting for 1 level loss)
                        {
                            // Calculate the position in the neighbor chunk where light will enter
                            int neighborX = dir == Vector.West ? 0 : 15;
                            var targetPos = new Vector(neighborX, y, z);

                            // Propagate with reduced level (1 level lost crossing chunk boundary)
                            SetLightAndSpread(targetPos, LightType.Sky, lightLevel - 1, neighbor);
                        }
                    }
                }
            }
            else // We're scanning along Z edge (North or South neighbor)
            {
                for (int x = 0; x < 16; x++)
                {
                    for (int y = -64; y < 320; y++)
                    {
                        var lightLevel = chunk.GetLightLevel(x, y, sourceEdgeZ, LightType.Sky);
                        if (lightLevel > 1) // Only propagate if there's meaningful light (accounting for 1 level loss)
                        {
                            // Calculate the position in the neighbor chunk where light will enter
                            int neighborZ = dir == Vector.North ? 0 : 15;
                            var targetPos = new Vector(x, y, neighborZ);

                            // Propagate with reduced level (1 level lost crossing chunk boundary)
                            SetLightAndSpread(targetPos, LightType.Sky, lightLevel - 1, neighbor);
                        }
                    }
                }
            }
        }
    }

    public static void SetLightAndSpread(Vector pos, LightType lt, int level, IChunk chunk, bool initial = false)
    {
        if (chunk is null) { return; }

        if (!initial)
        {
            int curLevel = chunk.GetLightLevel(pos, lt);
            if (curLevel >= level) { return; }
            chunk.SetLightLevel(pos, lt, level);
        }

        var highY = 320;

        // Light needs to go in the first empty section
        // too so neighbor chunks can place tree leaves
        // that are lit. Would subtract 4 here for negative
        // sections but 3 instead (also why 22 above instead 23).
        for (int csy = 22; csy >= 0; csy--)
        {
            if (!chunk.Sections[csy].IsEmpty)
            {
                highY = ((csy - 3) << 4) + 15;
                break;
            }
        }

        // Can spread up with no loss of level
        // as long as there is a neighbor that's non-transparent.
        for (int spreadY = 1; spreadY < highY - pos.Y; spreadY++)
        {
            foreach (Vector dir in Vector.CardinalDirs)
            {
                if (chunk.GetBlock(pos + (0, spreadY, 0) + dir) is IBlock b && !(b.IsLiquid || b.IsAir))
                {
                    if (chunk.GetLightLevel(pos + (0, spreadY, 0), lt) < level)
                    {
                        chunk.SetLightLevel(pos + (0, spreadY, 0), lt, level);
                    }
                    break;
                }
            }
        }

        // Spreading horizontally now, so 1 level lost.
        level--;
        if (level == 0) { return; }

        // Can spread in any cardinal direction and up/down.
        // No additional level lost for traveling vertically.
        foreach (Vector dir in Vector.CardinalDirs)
        {
            // If light would propagate to another chunk, bail out now. This is handled elsewhere.
            if (pos.X == 0 && dir == Vector.West ||
                pos.X == 15 && dir == Vector.East ||
                pos.Z == 0 && dir == Vector.North ||
                pos.Z == 15 && dir == Vector.South)
            {
                continue;
            }

            // Spread up
            for (int spreadY = 1; spreadY < (highY - pos.Y); spreadY++)
            {
                // To spread up, there must only be transparent blocks above the source
                var upBlock = chunk.GetBlock(pos + (0, spreadY, 0));
                if (!TagsRegistry.Block.Transparent.Entries.Contains(upBlock.RegistryId)) { break; }

                var scanPos = pos + dir + (0, spreadY, 0);
                if (TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(scanPos).RegistryId))
                {
                    if (TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(scanPos + Vector.Down).RegistryId))
                    {
                        chunk.SetLightLevel(scanPos, lt, level);
                    }
                    else
                    {
                        SetLightAndSpread(scanPos, lt, level, chunk);
                    }
                }
            }

            // Spread down
            // To spread down, the block above the adjacent must be transparent
            if (!TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(pos + dir + Vector.Up).RegistryId)) { continue; }

            // Find the first non-transparent block and set level
            // Calculate how far down we can go from current position to world lower bound (-64)
            int maxSpreadDown = pos.Y - (-64);
            for (int spreadY = 0; spreadY > -maxSpreadDown; spreadY--)
            {
                var scanPos = pos + dir + (0, spreadY, 0);
                if (!TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(scanPos).RegistryId))
                {
                    SetLightAndSpread(scanPos, lt, level, chunk);
                    break;
                }
                else
                {
                    chunk.SetLightLevel(scanPos, lt, level);
                }
            }
        }
    }
}

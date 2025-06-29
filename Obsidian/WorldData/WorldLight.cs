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
        for (int csy = 22; csy >= 0; csy--)
        {
            if (!chunk.Sections[csy].IsEmpty)
            {
                // Light needs to go in the first empty section
                // too so neighbor chunks can place tree leaves
                // that are lit. Would subtract 4 here for negative
                // sections but 3 instead (also why 22 above instead 23).
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
                    chunk.SetLightLevel(pos + (0, spreadY, 0), lt, level);
                    break;
                }
            }
        }

        level--;

        if (level == 0) { return; }

        // Can spread in any cardinal direction and up/down.
        // No level lost for travelling vertically.
        foreach (Vector dir in Vector.CardinalDirs)
        {
            // If light would propogate to another chunk, bail out now
            // TODO: don't bail out lol - get new chunk ref
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
                    if (!TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(scanPos + Vector.Down).RegistryId))
                    {
                        SetLightAndSpread(scanPos, lt, level, chunk);
                    } 
                    else
                    {
                        chunk.SetLightLevel(scanPos, lt, level);
                    }
                }
            }

            // Spread down
            // To spread down, the block above the adjacent must be transparent
            if (!TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(pos + dir + Vector.Up).RegistryId)) { continue; }

            // Find the first non-transparent block and set level
            for (int spreadY = 0; spreadY > (-64 - pos.Y); spreadY--)
            {
                var scanPos = pos + dir + (0, spreadY, 0);
                if (!TagsRegistry.Block.Transparent.Entries.Contains(chunk.GetBlock(scanPos).RegistryId))
                {
                    SetLightAndSpread(scanPos + Vector.Up, lt, level, chunk);
                    break;
                }
                else
                {
                    chunk.SetLightLevel(scanPos + Vector.Up, lt, level);
                }
            }
        }
    }
}

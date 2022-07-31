
namespace Obsidian.WorldData;

internal class WorldLight
{
    private readonly World world;

    public WorldLight(World world)
    {
        this.world = world;
    }

    public async Task ProcessSkyLightForChunk(Chunk chunk)
    {
        // No skylight for nether/end
        if (world.Name != "overworld") { return; }

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                var surfaceY = chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].GetHeight(x, z);
                for (int y = 319; y > surfaceY; y--)
                {
                    var secIndex = (y >> 4) + 4;
                    if (chunk.Sections[secIndex].IsEmpty)
                    {
                        y -= 15;
                    }
                    else
                    {
                        chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
                    }
                }

                var worldPos = new Vector(x + (chunk.X << 4), surfaceY, z + (chunk.Z << 4));
                await SetLightAndSpread(worldPos, LightType.Sky, 15);
            }
        }

        //TODO: Check neighboring chunks (if exist) for light on edges
    }

    public async Task SetLightAndSpread(Vector pos, LightType lt, int level)
    {
        var chunk = await world.GetChunkAsync(pos, false);
        if (chunk is null) { return; }
        if (!chunk.isGenerated) { return; }

        int curLevel = chunk.GetLightLevel(pos, lt);
        if (level <= curLevel) { return; }

        chunk.SetLightLevel(pos, lt, level);

        // Can spread up with no loss of level
        // as long as there is a neighbor that's non-transparent.
        for (int spreadY = 1; spreadY < 320 - pos.Y; spreadY++)
        {
            var secIndex = ((pos.Y + spreadY) >> 4) + 4;
            if (chunk.Sections[secIndex].IsEmpty) { break; }

            foreach (Vector dir in Vector.CardinalDirs)
            {
                if (await world.GetBlockAsync(pos + (0, spreadY, 0) + dir) is { IsTransparent: false })
                {
                    chunk.SetLightLevel(pos + (0, spreadY, 0), lt, level);
                }
            }
        }

        level--;

        if (level == 0) { return; }

        // Can spread in any cardinal direction and up/down.
        // No level lost for travelling vertically.
        foreach (Vector dir in Vector.CardinalDirs)
        {
            var highY = await world.GetWorldSurfaceHeightAsync(pos.X + dir.X, pos.Z + dir.Z) + 1;

            // Spread up
            for (int spreadY = 1; spreadY < (highY - pos.Y); spreadY++)
            {
                // To spread up, there must only be transparent blocks above the source
                var upBlock = chunk.GetBlock(pos + (0, spreadY, 0));
                if (!upBlock.IsTransparent) { break; }

                var scanPos = pos + dir + (0, spreadY, 0);
                if (await world.GetBlockAsync(scanPos) is { IsTransparent: true })
                {
                    if (await world.GetBlockAsync(scanPos + Vector.Down) is { IsTransparent: false })
                    {
                        await SetLightAndSpread(scanPos + Vector.Down, lt, level);
                    }
                }
            }

            // Spread down
            // To spread down, the block above the adjacent must be transparent
            if (await world.GetBlockAsync(pos + dir + Vector.Up) is { IsTransparent: false }) { continue; }

            // Find the first non-transparent block and set level
            for (int spreadY = 0; spreadY > (-64 - pos.Y); spreadY--)
            {
                var scanPos = pos + dir + (0, spreadY, 0);
                if (await world.GetBlockAsync(scanPos) is { IsTransparent: false })
                {
                    await SetLightAndSpread(scanPos, lt, level);
                    break;
                }
            }
        }
    }
}

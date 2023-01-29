using Obsidian.Registries;
using Obsidian.WorldData.Generators.Overworld.Decorators;

namespace Obsidian.WorldData.Generators;

public sealed class OverworldGenerator : IWorldGenerator
{
    private GenHelper helper;

    public string Id => "overworld";

    public async Task<Chunk> GenerateChunkAsync(int cx, int cz, Chunk? chunk = null)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.isGenerated)
            return chunk;
        if (helper is null)
            throw new NullReferenceException("GenHelper must not be null. Call Init()");

        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int worldX = bx + (cx << 4);
                int worldZ = bz + (cz << 4);
                //chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, (int)helper.Noise.Heightmap.GetValue(worldX, 0, worldZ));

                // Determine Biome
                if (bx % 4 == 0 && bz % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    var biome = (Biome)helper.Noise.Biome.GetValue(worldX, 0, worldZ);
                    for (int y = -64; y < 320; y += 4)
                    {
                        chunk.SetBiome(bx, y, bz, biome);
                    }
                }
                int maxY = -64;
                for (int y = -64; y < 320; y++)
                {
                    if (helper.Noise.IsTerrain(worldX, y, worldZ))
                    {
                        chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                        maxY = Math.Max(maxY, y);
                    }
                }
                chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, maxY);
            }
        }

        //ChunkBuilder.FillChunk(chunk);
        //ChunkBuilder.CarveCaves(helper, chunk);
        await OverworldDecorator.DecorateAsync(chunk, helper);

        chunk.isGenerated = true;
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
    }
}

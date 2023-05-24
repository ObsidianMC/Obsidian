using Obsidian.Registries;
using Obsidian.WorldData.Decorators;

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

                // Determine Biome
                if (bx % 4 == 0 && bz % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    var biome = (Biome)helper.Noise.Biome.GetValue(worldX, 0, worldZ);
                    for (int y = -64; y < 320; y += 4)
                    {
                        chunk.SetBiome(bx, y, bz, biome);
                    }
                }

                int terrainHeight = -64;
                // Search for the surface. Start at sea level...
                // If stone, scan upwards until 64 consecutive air
                // If air, scan downwards until 64 consecutive stone
                if (helper.Noise.IsTerrain(worldX, 64, worldZ))
                {
                    int airCount = 0;
                    for (int y = 64; y < 320; y++)
                    {
                        if (helper.Noise.IsTerrain(worldX, y, worldZ))
                        {
                            terrainHeight = y;
                            chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                        }
                        else
                        {
                            airCount++;
                        }
                        if (airCount == 30)
                        {
                            break;
                        }
                    }
                    for (int y = 64; y >= -64; y--)
                    {
                        chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                    }
                }
                else
                {
                    int solidCount = 0;
                    for (int y = 64; y >= -64; y--)
                    {
                        if (solidCount <= 30)
                        {
                            if (helper.Noise.IsTerrain(worldX, y, worldZ))
                            {
                                solidCount++;
                                chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                                terrainHeight = Math.Max(terrainHeight, y);
                            }
                            else
                            {
                                chunk.SetBlock(bx, y, bz, BlocksRegistry.Water);
                            }
                        }
                        else
                        {
                            chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                        }
                    }
                }

                chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, terrainHeight);
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

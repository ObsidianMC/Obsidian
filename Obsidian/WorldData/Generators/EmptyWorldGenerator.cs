using Obsidian.ChunkData;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators;

public class EmptyWorldGenerator : IWorldGenerator
{
    private static readonly Chunk empty;
    private static readonly Chunk spawn;

    public string Id => "obby-classic";

    static EmptyWorldGenerator()
    {
        spawn = new Chunk(0, 0);
        empty = new Chunk(0, 0);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                spawn.SetBlock(x, -60, z, BlocksRegistry.GrassBlock);
                spawn.SetBlock(x, -61, z, BlocksRegistry.Dirt);
                spawn.SetBlock(x, -62, z, BlocksRegistry.Dirt);
                spawn.SetBlock(x, -63, z, BlocksRegistry.Dirt);
                spawn.SetBlock(x, -64, z, BlocksRegistry.Bedrock);

                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    for (int y = -64; y < 320; y += 4)
                    {
                        spawn.SetBiome(x, y, z, Biome.Plains);
                        empty.SetBiome(x, y, z, Biome.TheVoid);
                    }
                }
            }
        }

        Heightmap motionBlockingHeightmap = empty.Heightmaps[HeightmapType.MotionBlocking];
        Heightmap motionBlockingHeightmapSpawn = spawn.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                motionBlockingHeightmap.Set(bx, bz, -64);
                motionBlockingHeightmapSpawn.Set(bx, bz, -64);
            }
        }

        empty.isGenerated = true;
        spawn.isGenerated = true;
    }

    public async Task<Chunk> GenerateChunkAsync(int x, int z, Chunk? chunk = null)
    {
        if (chunk is { isGenerated: true })
            return chunk;

        if (x == 0 && z == 0)
            return spawn.Clone(x, z);
        return empty.Clone(x, z);
    }

    public void Init(IWorld world) { }
}

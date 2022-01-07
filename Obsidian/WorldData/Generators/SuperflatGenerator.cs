using Obsidian.ChunkData;

namespace Obsidian.WorldData.Generators;

[WorldGenerator("superflat")]
public class SuperflatGenerator : WorldGenerator
{
    private static readonly Chunk model;

    static SuperflatGenerator()
    {
        model = new Chunk(0, 0);

        Block grass = new(Material.GrassBlock, 1);
        Block dirt = new(Material.Dirt);
        Block bedrock = new(Material.Bedrock);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                model.SetBlock(x, -60, z, grass);
                model.SetBlock(x, -61, z, dirt);
                model.SetBlock(x, -62, z, dirt);
                model.SetBlock(x, -63, z, dirt);
                model.SetBlock(x, -64, z, bedrock);

                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    for (int y = -64; y < 320; y += 4)
                    {
                        model.SetBiome(x, y, z, Biomes.Plains);
                    }
                }
            }
        }

        Heightmap motionBlockingHeightmap = model.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                motionBlockingHeightmap.Set(bx, bz, -64);
            }
        }

        model.isGenerated = true;
    }

    public SuperflatGenerator(string seed) : base()
    {
    }

    public override async Task<Chunk> GenerateChunkAsync(int x, int z, World world, Chunk? chunk = null)
    {
        if (chunk is { isGenerated: true })
            return chunk;

        return model.Clone(x, z);
    }
}

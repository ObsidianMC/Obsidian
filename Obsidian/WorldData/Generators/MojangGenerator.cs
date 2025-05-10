using Obsidian.API.World.Generator;


namespace Obsidian.WorldData.Generators;
internal class MojangGenerator : IWorldGenerator
{

    public string Id => "minecraft:mojang_generator";

    private ChunkBuilder _builder;

    public async Task<Chunk> GenerateChunkAsync(int cx, int cz, Chunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;

        chunk.chunkStatus = chunk.chunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.chunkStatus;

        if (ChunkGenStage.biomes <= stage && chunk.chunkStatus < ChunkGenStage.biomes)
        {
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    int worldX = x + (chunk.X << 4);
                    int worldZ = z + (chunk.Z << 4);

                    // Determine Biome
                    if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                    {
                        var biome = Biome.Plains;
                        for (int y = -64; y < 320; y += 4)
                        {
                            chunk.SetBiome(x, y, z, biome);
                        }
                    }
                }
            }



            
            chunk.chunkStatus = ChunkGenStage.biomes;
        }

        if (ChunkGenStage.surface <= stage && chunk.chunkStatus < ChunkGenStage.surface)
        {
            _builder.InitialShape(chunk, BlocksRegistry.GrassBlock);
            chunk.chunkStatus = ChunkGenStage.surface;
        }

        if (ChunkGenStage.carvers <= stage && chunk.chunkStatus < ChunkGenStage.carvers)
        {
            chunk.chunkStatus = ChunkGenStage.carvers;
        }

        if (ChunkGenStage.features <= stage && chunk.chunkStatus < ChunkGenStage.features)
        {
            chunk.chunkStatus = ChunkGenStage.features;
        }

        if (ChunkGenStage.heightmaps <= stage && chunk.chunkStatus < ChunkGenStage.heightmaps)
        {
            chunk.chunkStatus = ChunkGenStage.heightmaps;
        }

        if (ChunkGenStage.light <= stage && chunk.chunkStatus < ChunkGenStage.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            chunk.chunkStatus = ChunkGenStage.light;
        }

        chunk.chunkStatus = ChunkGenStage.full;
        return chunk;
    }
    public void Init(IWorld world)
    {
        _builder = new ChunkBuilder(world, "minecraft:overworld");
    }
}

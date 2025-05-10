using Obsidian.API.World.Generator;


namespace Obsidian.WorldData.Generators;
internal class MojangGenerator : IWorldGenerator
{

    public string Id => "minecraft:mojang_generator";

    private ChunkBuilder _builder;

    public ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage stage = ChunkGenStage.full)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return ValueTask.FromResult(chunk);

        chunk.SetChunkStatus(chunk.ChunkStatus == ChunkGenStage.empty ? ChunkGenStage.structure_references : chunk.ChunkStatus);

        if (ChunkGenStage.biomes <= stage && chunk.ChunkStatus < ChunkGenStage.biomes)
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
            chunk.SetChunkStatus(ChunkGenStage.biomes);
        }

        if (ChunkGenStage.surface <= stage && chunk.ChunkStatus < ChunkGenStage.surface)
        {
            _builder.InitialShape(chunk, BlocksRegistry.GrassBlock);
            chunk.SetChunkStatus(ChunkGenStage.surface);
        }

        if (ChunkGenStage.carvers <= stage && chunk.ChunkStatus < ChunkGenStage.carvers)
        {
            chunk.SetChunkStatus(ChunkGenStage.carvers);
        }

        if (ChunkGenStage.features <= stage && chunk.ChunkStatus < ChunkGenStage.features)
        {
            chunk.SetChunkStatus(ChunkGenStage.features);
        }

        if (ChunkGenStage.heightmaps <= stage && chunk.ChunkStatus < ChunkGenStage.heightmaps)
        {
            chunk.SetChunkStatus(ChunkGenStage.heightmaps);
        }

        if (ChunkGenStage.light <= stage && chunk.ChunkStatus < ChunkGenStage.full)
        {
            WorldLight.InitialFillSkyLight(chunk);
            chunk.SetChunkStatus(ChunkGenStage.light);
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        return ValueTask.FromResult(chunk);
    }
    public void Init(IWorld world)
    {
        _builder = new ChunkBuilder(world, "minecraft:overworld");
    }}

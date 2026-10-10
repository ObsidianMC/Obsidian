namespace Obsidian.WorldData.Generators;

public class SuperflatGenerator : ILevelGenerator
{
    private static readonly Chunk model = CreateModel();

    public string Id => "superflat";

    /// <summary>
    /// Builds the chunk every generated chunk is cloned from.
    /// </summary>
    private static Chunk CreateModel()
    {
        var chunk = new Chunk(0, 0);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                chunk.SetBlock(x, -60, z, BlocksRegistry.GrassBlock);
                chunk.SetBlock(x, -61, z, BlocksRegistry.Dirt);
                chunk.SetBlock(x, -62, z, BlocksRegistry.Dirt);
                chunk.SetBlock(x, -63, z, BlocksRegistry.Dirt);
                chunk.SetBlock(x, -64, z, BlocksRegistry.Bedrock);
                for (var y = -59; y < 320; y++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);

                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    for (int y = -64; y < 320; y += 4)
                    {
                        chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }
            }
        }

        Heightmap motionBlockingHeightmap = chunk.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                motionBlockingHeightmap.Set(bx, bz, -60);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage status = ChunkGenStage.full)
    {
        OverworldBuildRange.Ensure(chunk, this.Id);

        return chunk is { IsGenerated: true } ? ValueTask.FromResult(chunk) : ValueTask.FromResult(model.Clone(x, z));
    }

    public void Init(ILevel level) { }

    /// <summary>
    /// Players spawn on the grass at the world's center, which is the same everywhere.
    /// </summary>
    public ValueTask<VectorD?> FindSpawnPointAsync() => ValueTask.FromResult<VectorD?>(new VectorD(0.5, -59, 0.5));

}

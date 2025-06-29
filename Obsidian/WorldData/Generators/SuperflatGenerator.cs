namespace Obsidian.WorldData.Generators;

public class SuperflatGenerator : IWorldGenerator
{
    private static readonly Chunk model;

    public string Id => "superflat";

    static SuperflatGenerator()
    {
        model = new Chunk(0, 0);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                model.SetBlock(x, -60, z, BlocksRegistry.GrassBlock);
                model.SetBlock(x, -61, z, BlocksRegistry.Dirt);
                model.SetBlock(x, -62, z, BlocksRegistry.Dirt);
                model.SetBlock(x, -63, z, BlocksRegistry.Dirt);
                model.SetBlock(x, -64, z, BlocksRegistry.Bedrock);

                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    for (int y = -64; y < 320; y += 4)
                    {
                        model.SetBiome(x, y, z, Biome.Plains);
                    }
                }
            }
        }

        Heightmap motionBlockingHeightmap = model.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                motionBlockingHeightmap.Set(bx, bz, -60);
            }
        }

        model.SetChunkStatus(ChunkGenStage.full);
    }

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage status = ChunkGenStage.full) =>
        chunk is { IsGenerated: true } ? ValueTask.FromResult(chunk) : ValueTask.FromResult(model.Clone(x, z));

    public void Init(IWorld world) { }

}

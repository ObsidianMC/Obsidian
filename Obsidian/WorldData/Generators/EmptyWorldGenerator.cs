using Obsidian.API.World;

namespace Obsidian.WorldData.Generators;

public class EmptyWorldGenerator : ILevelGenerator
{
    private static readonly Chunk empty = CreateChunk(isSpawn: false);
    private static readonly Chunk spawn = CreateChunk(isSpawn: true);

    public string Id => "obby-classic";

    /// <summary>
    /// Builds the chunk every generated chunk is cloned from: a small platform for the spawn chunk, void elsewhere.
    /// </summary>
    private static Chunk CreateChunk(bool isSpawn)
    {
        var chunk = new Chunk(0, 0);

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (isSpawn)
                {
                    chunk.SetBlock(x, -60, z, BlocksRegistry.GrassBlock);
                    chunk.SetBlock(x, -61, z, BlocksRegistry.Dirt);
                    chunk.SetBlock(x, -62, z, BlocksRegistry.Dirt);
                    chunk.SetBlock(x, -63, z, BlocksRegistry.Dirt);
                    chunk.SetBlock(x, -64, z, BlocksRegistry.Bedrock);
                }

                if (x % 4 == 0 && z % 4 == 0) // Biomes are in 4x4x4 blocks. Do a 2D array for now and just copy it vertically.
                {
                    for (int y = -64; y < 320; y += 4)
                        chunk.SetBiome(x, y, z, isSpawn ? CodecRegistry.Biomes.Plains : CodecRegistry.Biomes.TheVoid);
                }
            }
        }

        Heightmap motionBlockingHeightmap = chunk.Heightmaps[HeightmapType.MotionBlocking];
        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                motionBlockingHeightmap.Set(bx, bz, -64);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);

        return chunk;
    }

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null, ChunkGenStage status = ChunkGenStage.full)
    {
        OverworldBuildRange.Ensure(chunk, this.Id);

        if (chunk is { IsGenerated: true })
            return ValueTask.FromResult(chunk);

        return x == 0 && z == 0 ? ValueTask.FromResult(spawn.Clone(x, z)) : ValueTask.FromResult(empty.Clone(x, z));
    }

    public void Init(ILevel level) { }
}

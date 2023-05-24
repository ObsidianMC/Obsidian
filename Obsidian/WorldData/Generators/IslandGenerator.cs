using Obsidian.Registries;
using Obsidian.WorldData.Decorators;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators;

public sealed class IslandGenerator : IWorldGenerator
{
    private GenHelper helper;
    private Module noiseGenerator;

    public string Id => "island";

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
                for (int y = -64; y < 320; y++)
                {
                    var val = noiseGenerator.GetValue(worldX, y, worldZ);
                    var isIsland = val > 9;
                    if (isIsland)
                    {
                        chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                    }
                }

                chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, 0);
            }
        }

        //ChunkBuilder.FillChunk(chunk);
        //ChunkBuilder.CarveCaves(helper, chunk);
        //await OverworldDecorator.DecorateAsync(chunk, helper);

        chunk.isGenerated = true;
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
        noiseGenerator = new Cell()
        {
            Type = Cell.CellType.Voronoi,
            Displacement = 10D,
            Frequency = 0.08,
            Seed = 12345
        };
    }
}

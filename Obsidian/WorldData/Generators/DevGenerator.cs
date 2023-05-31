using Obsidian.Registries;
using Obsidian.WorldData.Decorators;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators;

public sealed class DevGenerator : IWorldGenerator
{
    public string Id => "dev";
    private GenHelper? helper;
    private Module? noiseGenerator;
    private Random? r;
    private static readonly Biome[] biomes = new Biome[22] {
        Biome.Badlands,
        Biome.BambooJungle,
        Biome.BirchForest,
        Biome.DarkForest,
        Biome.Desert,
        Biome.ErodedBadlands,
        Biome.FlowerForest,
        Biome.Forest,
        Biome.FrozenPeaks,
        Biome.Grove,
        Biome.Jungle,
        Biome.MushroomFields,
        Biome.OldGrowthBirchForest,
        Biome.OldGrowthSpruceTaiga,
        Biome.Plains,
        Biome.Savanna,
        Biome.SnowySlopes,
        Biome.SnowyTaiga,
        Biome.StonyPeaks,
        Biome.SunflowerPlains,
        Biome.Taiga,
        Biome.WoodedBadlands
    };
    private static readonly IBlock[] hangingBlocks = new IBlock[4]
    {
        BlocksRegistry.Stone,
        BlocksRegistry.Cobblestone,
        BlocksRegistry.MossyCobblestone,
        BlocksRegistry.Andesite
    };

    public async Task<Chunk> GenerateChunkAsync(int cx, int cz, Chunk? chunk = null)
    {
        chunk ??= new Chunk(cx, cz);

        // Sanity checks
        if (chunk.IsGenerated)
            return chunk;
        if (helper is null)
            throw new NullReferenceException("GenHelper must not be null. Call Init()");

        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                int worldX = bx + (cx << 4);
                int worldZ = bz + (cz << 4);
                for (int y = -64; y < 320; y++)
                {
                    var noise = noiseGenerator!.GetValue(worldX, y, worldZ);
                    var noise2 = noiseGenerator!.GetValue(worldX+1000, y+1000, worldZ+1000);
                    if (noise > 1.2 && noise2 > 1.2)
                    {
                        chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                        chunk.Heightmaps[ChunkData.HeightmapType.MotionBlocking].Set(bx, bz, y);
                    }
                    if (bx % 4 == 0 && bz % 4 == 0 & y % 4 == 0)
                    {
                        chunk.SetBiome(bx, y, bz, Biome.StonyShore);
                    }
                }
            }
        }


        chunk.chunkStatus = ChunkStatus.full;
        return chunk;
    }

    public void Init(IWorld world)
    {
        helper = new GenHelper(world);
        r = new Random(helper.Seed);
        noiseGenerator = new ScalePoint()
        {
            XScale = 1.0D,
            ZScale = 1.0D,
            YScale = 1.0D,
            Source0 = new Perlin
            {
                Frequency = 0.063,
                Lacunarity = 1.3,
                OctaveCount = 3,
                Persistence = 0.9,
                Quality = SharpNoise.NoiseQuality.Fast,
                Seed = helper.Seed
            }
        };
    }
}

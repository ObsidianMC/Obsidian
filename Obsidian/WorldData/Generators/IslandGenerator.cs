using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World;
using Obsidian.WorldData.Decorators;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators;

public sealed class IslandGenerator : ILevelGenerator
{
    public string Id => "islands";
    private GenHelper? helper;
    private ILevel? world;
    private Module? noiseGenerator;
    private Random? r;
    private static readonly BiomeCodec[] biomes = [
        CodecRegistry.Biomes.Badlands,
        CodecRegistry.Biomes.BambooJungle,
        CodecRegistry.Biomes.BirchForest,
        CodecRegistry.Biomes.DarkForest,
        CodecRegistry.Biomes.Desert,
        CodecRegistry.Biomes.ErodedBadlands,
        CodecRegistry.Biomes.FlowerForest,
        CodecRegistry.Biomes.Forest,
        CodecRegistry.Biomes.FrozenPeaks,
        CodecRegistry.Biomes.Grove,
        CodecRegistry.Biomes.Jungle,
        CodecRegistry.Biomes.MushroomFields,
        CodecRegistry.Biomes.OldGrowthBirchForest,
        CodecRegistry.Biomes.OldGrowthSpruceTaiga,
        CodecRegistry.Biomes.Plains,
        CodecRegistry.Biomes.Savanna,
        CodecRegistry.Biomes.SnowySlopes,
        CodecRegistry.Biomes.SnowyTaiga,
        CodecRegistry.Biomes.StonyPeaks,
        CodecRegistry.Biomes.SunflowerPlains,
        CodecRegistry.Biomes.Taiga,
        CodecRegistry.Biomes.WoodedBadlands
    ];
    private static readonly IBlock[] hangingBlocks =
    [
        BlocksRegistry.Stone,
        BlocksRegistry.Cobblestone,
        BlocksRegistry.MossyCobblestone,
        BlocksRegistry.Andesite
    ];

    public async ValueTask<IChunk> GenerateChunkAsync(int cx, int cz, IChunk? chunk = null, ChunkGenStage status = ChunkGenStage.full)
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
                    // The integer of a noise float b/w -100.0 and 100.0 will be used to determine if it's an
                    // island and what type of island it is.
                    var noiseVal = noiseGenerator!.GetValue(worldX, y, worldZ);
                    var islandType = (int)noiseVal;
                    var isIsland = islandType % 10 == 0;
                    var biome = biomes[(islandType / 10) + 11];

                    // The decimal of the noise will determine the density of the island.
                    // Islands become less dense toward the Y axis extremes, and more dense
                    // toward the center.
                    var dec = Math.Abs(noiseVal - Math.Truncate(noiseVal));
                    var yPct = (y + 64) / 384.0;
                    var yDensity = 1 - (Math.Abs(dec - 0.5) * 2);
                    var yDensityMap = -1 * Math.Abs(6 * yPct - 3) + 1;

                    if (isIsland & yDensity < yDensityMap)
                    {
                        if (bx % 4 == 0 && bz % 4 == 0 & y % 4 == 0)
                        {
                            chunk.SetBiome(bx, y, bz, biome);
                        }
                        chunk.SetBlock(bx, y, bz, BlocksRegistry.Stone);
                        chunk.Heightmaps[HeightmapType.MotionBlocking].Set(bx, bz, y);
                        chunk.Heightmaps[HeightmapType.WorldSurfaceWG].Set(bx, bz, y);
                    }
                    else
                    {
                        if (bx % 4 == 0 && bz % 4 == 0 & y % 4 == 0)
                        {
                            chunk.SetBiome(bx, y, bz, CodecRegistry.Biomes.StonyShore);
                        }
                    }
                }
            }
        }


        for (int bx = 0; bx < 16; bx++)
        {
            for (int bz = 0; bz < 16; bz++)
            {
                // Scan up from the bottom to find island bottoms
                for (int by = 20; by < 200; by++)
                {
                    var pos = new Vector(bx, by, bz);
                    var isHanging = chunk.GetBlock(pos).Is(Material.Stone) && chunk.GetBlock(pos + Vector.Down).IsAir;
                    if (isHanging && r!.Next(0, 5) == 0)
                    {
                        IBlock b = hangingBlocks[r!.Next(0, 4)];
                        int length = r!.Next(0, 5);
                        for (int y = 0; y < length; y++)
                        {
                            chunk.SetBlock(pos + (Vector.Down * y), b);
                        }
                    }
                }
                // Scan down from the top to decorate the surface
                for (int by = 200; by > 20; by--)
                {
                    var pos = new Vector(bx, by, bz);
                    var worldPos = new Vector(bx + (cx << 4), by, bz + (cz << 4));
                    var isSurface = !chunk.GetBlock(pos).IsAir && chunk.GetBlock(pos + Vector.Up).IsAir;
                    if (isSurface)
                    {
                        var biome = chunk.GetBiome(pos + Vector.Down);
                        IDecorator decorator = DecoratorFactory.GetDecorator(biome, chunk, worldPos, helper);
                        decorator.Decorate();
                        await OverworldDecorator.GenerateTreesAsync(worldPos, decorator.Features, helper);
                        await OverworldDecorator.GenerateFloraAsync(worldPos, decorator.Features, helper, chunk);
                    }
                }
            }
        }

        Lighting.InitialFillSkyLight(chunk);
        await Lighting.LightFromNeighbors(chunk, world);
        chunk.SetChunkStatus(ChunkGenStage.light);
        await Lighting.LightToNeighbors(chunk, world);
        chunk.SetChunkStatus(ChunkGenStage.full);
        return chunk;
    }

    public void Init(ILevel world)
    {
        this.world = world;
        helper = new GenHelper(world);
        r = new Random(helper.Seed);
        noiseGenerator = new Turbulence()
        {
            Frequency = 0.0987,
            Power = 1.1,
            Roughness = 2,
            Seed = helper.Seed + 2,
            Source0 = new ScalePoint()
            {
                XScale = 0.2D,
                ZScale = 0.2D,
                YScale = 0.5D,
                Source0 = new Cell()
                {
                    Type = Cell.CellType.Manhattan,
                    Displacement = 100D,
                    Frequency = 0.08D,
                    Seed = helper.Seed + 1
                }
            }
        };
    }
}

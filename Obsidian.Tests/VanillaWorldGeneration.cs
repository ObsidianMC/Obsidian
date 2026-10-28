using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.DensityFunctions;
using Obsidian.API.World.Generator.Noise;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Lighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for the Mojang (vanilla) generator, replaying the references in <c>Assets/vanilla_worldgen.json</c>
/// (<see cref="VanillaWorldgenCaptures"/>). Vanilla's own world generation recorded them with the same seeds; re-record
/// them with <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c> (<c>docs/vanilla-captures.md</c>).
/// </summary>
[Collection(WorldgenCollection.Name)]
public class VanillaWorldGeneration(WorldgenFixture worldgen)
{
    [Theory]
    [InlineData("12345", 12345L)]
    [InlineData(" -4172144997902289642 ", -4172144997902289642L)]
    [InlineData("Obsidian", 416515707L)] // "Obsidian".hashCode() in Java
    public void ParsesSeedsLikeVanilla(string seed, long expected) => Assert.Equal(expected, RandomState.ParseSeed(seed));

    [Fact]
    public void UnboundRegistryNoiseSamplesAsSeedZero()
    {
        // Registry noises aren't bound to a world; sampling them must bind lazily instead of recursing.
        var unbound = NoiseRegistry.Noises.Temperature;
        var seedZero = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], 0L).GetOrCreateNoise("minecraft:temperature");

        Assert.Equal(seedZero.GetValue(12.5, 0.0, -40.0), unbound.GetValue(12.5, 0.0, -40.0));
        Assert.Equal(seedZero.MaxValue, unbound.MaxValue);
    }

    // Vanilla: RandomState.create(overworld, seed).router().<field>().compute(new SinglePointContext(x, y, z)).
    public static TheoryData<long, int, int, int, double, double, double, double, double> RouterCases()
    {
        var cases = new TheoryData<long, int, int, int, double, double, double, double, double>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("router"))
        {
            cases.Add(entry.GetProperty("seed").GetInt64(), entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(),
                entry.GetProperty("z").GetInt32(), entry.GetProperty("continents").GetDouble(), entry.GetProperty("erosion").GetDouble(),
                entry.GetProperty("depth").GetDouble(), entry.GetProperty("finalDensity").GetDouble(),
                entry.GetProperty("preliminarySurfaceLevel").GetDouble());
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(RouterCases))]
    public void NoiseRouterMatchesVanilla(long seed, int x, int y, int z, double continents, double erosion, double depth,
        double finalDensity, double preliminarySurfaceLevel)
    {
        var router = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], seed).Router;

        Assert.Equal(continents, router.Continents.GetValue(x, y, z));
        Assert.Equal(erosion, router.Erosion.GetValue(x, y, z));
        Assert.Equal(depth, router.Depth.GetValue(x, y, z));
        Assert.Equal(finalDensity, router.FinalDensity.GetValue(x, y, z));
        Assert.Equal(preliminarySurfaceLevel, router.PreliminarySurfaceLevel.GetValue(x, y, z));
    }

    // Vanilla: MultiNoiseBiomeSource (overworld preset).getNoiseBiome(quartX, quartY, quartZ, sampler).
    public static TheoryData<long, int, int, int, string> BiomeCases()
    {
        var cases = new TheoryData<long, int, int, int, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("biomes"))
        {
            cases.Add(entry.GetProperty("seed").GetInt64(), entry.GetProperty("quartX").GetInt32(), entry.GetProperty("quartY").GetInt32(),
                entry.GetProperty("quartZ").GetInt32(), entry.GetProperty("biome").GetString()!);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(BiomeCases))]
    public void BiomesMatchVanilla(long seed, int quartX, int quartY, int quartZ, string expected)
    {
        var biomeSource = MultiNoiseBiomeSource.Overworld(new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], seed));

        Assert.Equal(expected, biomeSource.GetNoiseBiome(quartX, quartY, quartZ).Name);
    }

    // SHA-256 of the block names (y, z, x order) and quart biomes (y, z, x order) of the chunk after vanilla's
    // biomes, noise and surface steps. Covers aquifers (water, lava), ore veins, bedrock and deepslate gradients.
    public static TheoryData<long, int, int, string, string> SurfaceChunkCases()
    {
        var cases = new TheoryData<long, int, int, string, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("chunks"))
        {
            cases.Add(entry.GetProperty("seed").GetInt64(), entry.GetProperty("chunkX").GetInt32(), entry.GetProperty("chunkZ").GetInt32(),
                entry.GetProperty("blocks").GetString()!, entry.GetProperty("biomes").GetString()!);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(SurfaceChunkCases))]
    public void ChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks, string expectedBiomes)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, seed, structures: false);
        var chunk = new Chunk(chunkX, chunkZ);

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);

        var biomes = new StringBuilder();
        for (var quartY = -16; quartY < 80; quartY++)
            for (var quartZ = 0; quartZ < 4; quartZ++)
                for (var quartX = 0; quartX < 4; quartX++)
                    biomes.Append(chunk.GetBiome(quartX << 2, quartY << 2, quartZ << 2).Name).Append('\n');

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunk)));
        Assert.Equal(expectedBiomes, Sha256(biomes.ToString(0, biomes.Length - 1)));
    }

    [Fact(DisplayName = "Noise settings read at runtime from vanilla's files generate vanilla's chunks")]
    public void RuntimeNoiseSettingsGenerateLikeVanilla()
    {
        // Every noise settings, density function and noise read from the files the generator compiles, like a
        // plugin's are, with no generated entry in them.
        var noise = WorldgenNoiseReading.ReadVanillaFiles(out var logger);
        Assert.Empty(logger.Messages);

        // The golden chunks of ChunkMatchesVanilla and CarvedChunkMatchesVanilla.
        var overworld = new ChunkBuilder(Runtime(MojangDimension.Overworld), 12345L, generateStructures: false);
        var surface = new Chunk(-100, 57);

        overworld.PopulateBiomes(surface);
        overworld.Generate3DTerrain(surface);
        overworld.ApplySurfaceRules(surface);

        Assert.Equal(VanillaWorldgenCaptures.Chunk("chunks", 12345L, -100, 57).GetProperty("blocks").GetString(), Sha256(BlockNames(surface)));

        var carved = new Chunk(-5, -5);
        overworld.PopulateBiomes(carved);
        overworld.Generate3DTerrain(carved);
        overworld.ApplySurfaceRules(carved);
        overworld.ApplyCarvers(carved);

        Assert.Equal(VanillaWorldgenCaptures.Chunk("carvedChunks", 12345L, -5, -5).GetProperty("blocks").GetString(), Sha256(BlockNames(carved)));

        // The nether's legacy settings, against the generated settings' chunk.
        var nether = new ChunkBuilder(Runtime(MojangDimension.Nether), 12345L, generateStructures: false);
        var generated = worldgen.Builder(MojangDimension.Nether, 12345L, structures: false);
        var expected = new Chunk(0, 0, MojangDimension.Nether.MinY, MojangDimension.Nether.Height);
        var actual = new Chunk(0, 0, MojangDimension.Nether.MinY, MojangDimension.Nether.Height);

        foreach (var (builder, chunk) in new[] { (generated, expected), (nether, actual) })
        {
            builder.PopulateBiomes(chunk);
            builder.Generate3DTerrain(chunk);
            builder.ApplySurfaceRules(chunk);
            builder.ApplyCarvers(chunk);
        }

        Assert.Equal(BlockNames(expected), BlockNames(actual));

        MojangDimension Runtime(MojangDimension vanilla) => new(
            vanilla.Identifier,
            vanilla.DimensionType.Name,
            vanilla.NoiseSettings,
            noise.NoiseSettings[vanilla.NoiseSettings!],
            vanilla.CreateBiomeSource,
            () => vanilla.PossibleBiomes);
    }

    // A seed's chunk and the SHA-256 a section of the fixture holds for it.
    private static TheoryData<long, int, int, string> ChunkHashes(string section, string hash)
    {
        var cases = new TheoryData<long, int, int, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases(section))
        {
            cases.Add(entry.GetProperty("seed").GetInt64(), entry.GetProperty("chunkX").GetInt32(), entry.GetProperty("chunkZ").GetInt32(),
                entry.GetProperty(hash).GetString()!);
        }

        return cases;
    }

    // A dimension's chunk and the SHA-256 a section of the fixture holds for it.
    private static TheoryData<string, long, int, int, string> DimensionChunkHashes(string section, string hash)
    {
        var cases = new TheoryData<string, long, int, int, string>();
        foreach (var entry in VanillaWorldgenCaptures.Cases(section))
        {
            cases.Add(entry.GetProperty("dimension").GetString()!, entry.GetProperty("seed").GetInt64(), entry.GetProperty("chunkX").GetInt32(),
                entry.GetProperty("chunkZ").GetInt32(), entry.GetProperty(hash).GetString()!);
        }

        return cases;
    }

    // SHA-256 of the block names (y, z, x order) after vanilla's carvers step.
    public static TheoryData<long, int, int, string> CarvedChunkCases() => ChunkHashes("carvedChunks", "blocks");

    [Theory]
    [MemberData(nameof(CarvedChunkCases))]
    public void CarvedChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, seed, structures: false);
        var chunk = new Chunk(chunkX, chunkZ);

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);
        builder.ApplyCarvers(chunk);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunk)));
    }

    // SHA-256 of the block names (y, z, x order) after vanilla decorates the chunk, with its neighbors at the carvers step.
    public static TheoryData<long, int, int, string> DecoratedChunkCases() => ChunkHashes("decoratedChunks", "blocks");

    [Theory]
    [MemberData(nameof(DecoratedChunkCases))]
    public void DecoratedChunkMatchesVanilla(long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, seed, structures: false);
        var area = new Dictionary<(int X, int Z), IChunk>();

        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                var neighbor = new Chunk(chunkX + dx, chunkZ + dz);
                builder.PopulateBiomes(neighbor);
                builder.Generate3DTerrain(neighbor);
                builder.ApplySurfaceRules(neighbor);
                builder.ApplyCarvers(neighbor);
                area[(neighbor.X, neighbor.Z)] = neighbor;
            }
        }

        builder.Decorate(area, chunkX, chunkZ);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(area[(chunkX, chunkZ)])));
    }

    // SHA-256 of the block names (y, z, x order) of a full chunk in a vanilla server world with structures disabled.
    // These chunks don't depend on the order their neighbors are decorated in.
    public static TheoryData<string, long, int, int, string> FullChunkCases() => DimensionChunkHashes("fullChunks", "blocks");

    [Theory]
    [MemberData(nameof(FullChunkCases))]
    public void FullChunkMatchesVanilla(string dimensionName, long seed, int chunkX, int chunkZ, string expectedBlocks)
    {
        var dimension = dimensionName == "nether" ? MojangDimension.Nether : MojangDimension.End;
        var chunks = worldgen.Chunks(dimension, seed, structures: false);

        Assert.Equal(expectedBlocks, Sha256(BlockNames(chunks.Get(chunkX, chunkZ))));
    }

    // Vanilla: the spawn in level.dat of a new server world with structures disabled.
    public static TheoryData<long, int, int, int> SpawnCases()
    {
        var cases = new TheoryData<long, int, int, int>();
        foreach (var entry in VanillaWorldgenCaptures.Cases("spawns"))
            cases.Add(entry.GetProperty("seed").GetInt64(), entry.GetProperty("x").GetInt32(), entry.GetProperty("y").GetInt32(), entry.GetProperty("z").GetInt32());

        return cases;
    }

    [Theory]
    [MemberData(nameof(SpawnCases))]
    public void SpawnMatchesVanilla(long seed, int x, int y, int z)
    {
        var builder = worldgen.Builder(MojangDimension.Overworld, seed, structures: false);
        var chunks = worldgen.Chunks(MojangDimension.Overworld, seed, structures: false);
        var (climateX, climateZ) = builder.FindClimateSpawn();

        Vector? spawn = null;
        foreach (var (dx, dz) in SpawnFinder.SpiralOffsets())
        {
            spawn = SpawnFinder.FindSpawnInChunk(chunks.Get((climateX >> 4) + dx, (climateZ >> 4) + dz), hasCeiling: false);
            if (spawn is not null)
                break;
        }

        Assert.Equal(new Vector(x, y, z), spawn);
    }

    // SHA-256 of the chunk's sky light arrays, then its block light arrays (one per section, bottom up, in vanilla's nibble
    // layout), once vanilla has lit the chunk and its neighbors. The 7x7 chunks around it are carved and the 5x5 around
    // it decorated first, z then x ascending, so the 3x3 around it has its final blocks; vanilla generates and lights the
    // same area in that order.
    public static TheoryData<string, long, int, int, string> LitChunkCases() => DimensionChunkHashes("litChunks", "light");

    [Theory]
    [MemberData(nameof(LitChunkCases))]
    public void LitChunkMatchesVanilla(string dimensionName, long seed, int chunkX, int chunkZ, string expectedLight)
    {
        var dimension = dimensionName switch
        {
            "nether" => MojangDimension.Nether,
            "end" => MojangDimension.End,
            _ => MojangDimension.Overworld
        };
        var builder = worldgen.Builder(dimension, seed, structures: false);
        var area = new Dictionary<(int X, int Z), IChunk>();

        for (var dz = -3; dz <= 3; dz++)
        {
            for (var dx = -3; dx <= 3; dx++)
            {
                var chunk = new Chunk(chunkX + dx, chunkZ + dz, dimension.MinY, dimension.Height);
                builder.PopulateBiomes(chunk);
                builder.Generate3DTerrain(chunk);
                builder.ApplySurfaceRules(chunk);
                builder.ApplyCarvers(chunk);
                area[(chunk.X, chunk.Z)] = chunk;
            }
        }

        for (var dz = -2; dz <= 2; dz++)
        {
            for (var dx = -2; dx <= 2; dx++)
                builder.Decorate(area, chunkX + dx, chunkZ + dz);
        }

        // The order chunks are lit in doesn't change the result; the center is lit halfway through.
        var lit = new List<IChunk>();
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var chunk = area[(chunkX + dx, chunkZ + dz)];
                LightEngine.LightChunk(chunk, lit.Where(other => Math.Abs(other.X - chunk.X) <= 1 && Math.Abs(other.Z - chunk.Z) <= 1),
                    hasSkyLight: dimension != MojangDimension.Nether);
                lit.Add(chunk);
            }
        }

        var sections = area[(chunkX, chunkZ)].Sections.ToArray();
        var light = sections.SelectMany(section => section.SkyLightArray.ToArray())
            .Concat(sections.SelectMany(section => section.BlockLightArray.ToArray()))
            .ToArray();

        Assert.Equal(expectedLight, Convert.ToHexStringLower(SHA256.HashData(light)));

        // Runtime edits must converge back to the captured vanilla light, including decreases in loaded neighbors.
        var runtime = new RuntimeLightEngine(new System.Threading.Lock(), () => dimension.HasSkyLight);
        var originals = new Dictionary<IChunk, byte[]>();
        foreach (var chunk in lit)
        {
            originals[chunk] = chunk.Sections.ToArray()
                .SelectMany(section => section.SkyLightArray.ToArray().Concat(section.BlockLightArray.ToArray()))
                .ToArray();
            runtime.Track(chunk);
        }

        runtime.Flush();
        var center = area[(chunkX, chunkZ)];
        var position = new Vector((chunkX << 4) + 15, dimension.MinY + dimension.Height / 2, (chunkZ << 4) + 8);
        var originalBlock = center.GetBlock(position);
        center.SetBlock(position, Obsidian.Registries.BlocksRegistry.Glowstone);
        runtime.Flush();
        center.SetBlock(position, originalBlock);
        runtime.Flush();
        foreach (var chunk in lit)
        {
            var restored = chunk.Sections.ToArray()
                .SelectMany(section => section.SkyLightArray.ToArray().Concat(section.BlockLightArray.ToArray()))
                .ToArray();
            Assert.Equal(originals[chunk], restored);
        }
    }

    [Fact]
    public void Cache2DOfYDependentFunctionKeepsFirstCornerOfColumn()
    {
        // Vanilla's cache_2d keeps its argument's value at the first position sampled in a column, which is the lowest
        // corner, even when the argument varies with Y. A range choice sending upper corners to the same cache first
        // must not change which corner primes it.
        var y = new YClampedGradientDensityFunction { FromY = -64, ToY = 320, FromValue = -64, ToValue = 320 };
        var cache = new Cache2DDensityFunction { Argument = y };
        var finalDensity = new InterpolatedDensityFunction
        {
            Argument = new RangeChoiceDensityFunction { Input = y, MinInclusive = 0, MaxExclusive = 1000, WhenInRange = cache, WhenOutOfRange = cache }
        };

        var overworld = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
        var zero = new ConstantDensityFunction { Argument = 0 };
        var settings = new NoiseSetting
        {
            DefaultBlock = overworld.DefaultBlock,
            DefaultFluid = overworld.DefaultFluid,
            Noise = overworld.Noise,
            SeaLevel = overworld.SeaLevel,
            NoiseRouter = new NoiseRouter
            {
                Barrier = zero, Continents = zero, Depth = zero, Erosion = zero, FinalDensity = finalDensity, FluidLevelFloodedness = zero,
                FluidLevelSpread = zero, PreliminarySurfaceLevel = zero, Lava = zero, Ridges = zero, Temperature = zero, Vegetation = zero,
                VeinGap = zero, VeinRidged = zero, VeinToggle = zero
            }
        };

        var chunk = new NoiseChunk(new RandomState(settings, 0L), 0, 0);
        var values = new double[chunk.CellSize];
        for (var cellY = 0; cellY < chunk.CellCountY; cellY++)
        {
            chunk.FillFinalDensity(0, cellY, 0, values);
            Assert.All(values, value => Assert.Equal(-64.0, value));
        }
    }

    private static string BlockNames(IChunk chunk)
    {
        var blocks = new StringBuilder();
        for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
            for (var z = 0; z < 16; z++)
                for (var x = 0; x < 16; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    blocks.Append(block.IsAir ? "minecraft:air" : block.UnlocalizedName).Append('\n');
                }

        return blocks.ToString(0, blocks.Length - 1);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

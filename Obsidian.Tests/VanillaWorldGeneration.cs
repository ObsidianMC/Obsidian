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
using System.Text.Json;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for the Mojang (vanilla) generator, replaying the references in <c>Assets/vanilla_worldgen.json</c>
/// (<see cref="VanillaWorldgenCaptures"/>). Vanilla 1.21.11's own world generation recorded them with the same seeds;
/// they can't be re-recorded on 26.3, and Phase 5 replaces them with 26.3 captures (<c>docs/vanilla-captures.md</c>).
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
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
    [Fact]
    public void NoiseRouterMatchesVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("router"), PointName, AssertRouter);

    private static void AssertRouter(JsonElement entry)
    {
        var (x, y, z) = (Int(entry, "x"), Int(entry, "y"), Int(entry, "z"));

        var router = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], Seed(entry)).Router;

        Assert.Equal(entry.GetProperty("continents").GetDouble(), router.Continents.GetValue(x, y, z));
        Assert.Equal(entry.GetProperty("erosion").GetDouble(), router.Erosion.GetValue(x, y, z));
        Assert.Equal(entry.GetProperty("depth").GetDouble(), router.Depth.GetValue(x, y, z));
        Assert.Equal(entry.GetProperty("finalDensity").GetDouble(), router.FinalDensity.GetValue(x, y, z));
        Assert.Equal(
            entry.GetProperty("preliminarySurfaceLevel").GetDouble(),
            router.PreliminarySurfaceLevel.GetValue(x, y, z));
    }

    // Vanilla: MultiNoiseBiomeSource (overworld preset).getNoiseBiome(quartX, quartY, quartZ, sampler).
    [Fact]
    public void BiomesMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("biomes"), QuartName, AssertBiome);

    private static void AssertBiome(JsonElement entry)
    {
        var random = new RandomState(NoiseRegistry.NoiseSettings.All["minecraft:overworld"], Seed(entry));
        var biomeSource = MultiNoiseBiomeSource.Overworld(random);

        var biome = biomeSource.GetNoiseBiome(Int(entry, "quartX"), Int(entry, "quartY"), Int(entry, "quartZ"));

        Assert.Equal(entry.GetProperty("biome").GetString(), biome.Name);
    }

    // SHA-256 of the block names (y, z, x order) and quart biomes (y, z, x order) of the chunk after vanilla's
    // biomes, noise and surface steps. Covers aquifers (water, lava), ore veins, bedrock and deepslate gradients.
    [Fact]
    public void ChunksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("chunks"), ChunkName, this.AssertChunk);

    private void AssertChunk(JsonElement entry)
    {
        var builder = this.OverworldBuilder(entry);
        var chunk = new Chunk(Int(entry, "chunkX"), Int(entry, "chunkZ"));

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);

        var biomes = new StringBuilder();
        for (var quartY = -16; quartY < 80; quartY++)
        {
            for (var quartZ = 0; quartZ < 4; quartZ++)
            {
                for (var quartX = 0; quartX < 4; quartX++)
                    biomes.Append(chunk.GetBiome(quartX << 2, quartY << 2, quartZ << 2).Name).Append('\n');
            }
        }

        Assert.Equal(entry.GetProperty("blocks").GetString(), Sha256(BlockNames(chunk)));
        Assert.Equal(entry.GetProperty("biomes").GetString(), Sha256(biomes.ToString(0, biomes.Length - 1)));
    }

    [Fact(DisplayName = "Noise settings read at runtime from vanilla's files generate vanilla's chunks")]
    public void RuntimeNoiseSettingsGenerateLikeVanilla()
    {
        // Every noise settings, density function and noise read from the files the generator compiles, like a
        // plugin's are, with no generated entry in them.
        var noise = WorldgenNoiseReading.ReadVanillaFiles(out var logger);
        Assert.Empty(logger.Messages);

        // The golden chunks of ChunksMatchVanilla and CarvedChunksMatchVanilla.
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

    // SHA-256 of the block names (y, z, x order) after vanilla's carvers step.
    [Fact]
    public void CarvedChunksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("carvedChunks"), ChunkName, this.AssertCarvedChunk);

    private void AssertCarvedChunk(JsonElement entry)
    {
        var builder = this.OverworldBuilder(entry);
        var chunk = new Chunk(Int(entry, "chunkX"), Int(entry, "chunkZ"));

        builder.PopulateBiomes(chunk);
        builder.Generate3DTerrain(chunk);
        builder.ApplySurfaceRules(chunk);
        builder.ApplyCarvers(chunk);

        Assert.Equal(entry.GetProperty("blocks").GetString(), Sha256(BlockNames(chunk)));
    }

    // SHA-256 of the block names (y, z, x order) after vanilla decorates the chunk, with its neighbors at the carvers step.
    [Fact]
    public void DecoratedChunksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("decoratedChunks"), ChunkName, this.AssertDecoratedChunk);

    private void AssertDecoratedChunk(JsonElement entry)
    {
        var builder = this.OverworldBuilder(entry);
        var (chunkX, chunkZ) = (Int(entry, "chunkX"), Int(entry, "chunkZ"));
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

        Assert.Equal(entry.GetProperty("blocks").GetString(), Sha256(BlockNames(area[(chunkX, chunkZ)])));
    }

    // SHA-256 of the block names (y, z, x order) of a full chunk in a vanilla server world with structures disabled.
    // These chunks don't depend on the order their neighbors are decorated in.
    [Fact]
    public void FullChunksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("fullChunks"), DimensionChunkName, this.AssertFullChunk);

    private void AssertFullChunk(JsonElement entry)
    {
        var dimension = Dimension(entry);
        var chunks = worldgen.Chunks(dimension, Seed(entry), structures: false);

        var chunk = chunks.Get(Int(entry, "chunkX"), Int(entry, "chunkZ"));

        Assert.Equal(entry.GetProperty("blocks").GetString(), Sha256(BlockNames(chunk)));
    }

    // Vanilla: the spawn in level.dat of a new server world with structures disabled.
    [Fact]
    public void SpawnMatchesVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("spawns"), SeedName, this.AssertSpawn);

    private void AssertSpawn(JsonElement entry)
    {
        var builder = this.OverworldBuilder(entry);
        var chunks = worldgen.Chunks(MojangDimension.Overworld, Seed(entry), structures: false);
        var (climateX, climateZ) = builder.FindClimateSpawn();

        Vector? spawn = null;
        foreach (var (dx, dz) in SpawnFinder.SpiralOffsets())
        {
            spawn = SpawnFinder.FindSpawnInChunk(chunks.Get((climateX >> 4) + dx, (climateZ >> 4) + dz), hasCeiling: false);
            if (spawn is not null)
                break;
        }

        Assert.Equal(new Vector(Int(entry, "x"), Int(entry, "y"), Int(entry, "z")), spawn);
    }

    // SHA-256 of the chunk's sky light arrays, then its block light arrays (one per section, bottom up, in vanilla's nibble
    // layout), once vanilla has lit the chunk and its neighbors. The 7x7 chunks around it are carved and the 5x5 around
    // it decorated first, z then x ascending, so the 3x3 around it has its final blocks; vanilla generates and lights the
    // same area in that order.
    [Fact]
    public void LitChunksMatchVanilla() =>
        FixtureCases.AssertAll(VanillaWorldgenCaptures.Cases("litChunks"), DimensionChunkName, this.AssertLitChunk);

    private void AssertLitChunk(JsonElement entry)
    {
        var dimension = Dimension(entry);
        var (chunkX, chunkZ) = (Int(entry, "chunkX"), Int(entry, "chunkZ"));
        var builder = worldgen.Builder(dimension, Seed(entry), structures: false);
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

        Assert.Equal(entry.GetProperty("light").GetString(), Convert.ToHexStringLower(SHA256.HashData(light)));

        AssertRuntimeLightConverges(dimension, lit, area[(chunkX, chunkZ)]);
    }

    // Runtime edits must converge back to the captured vanilla light, including decreases in loaded neighbors.
    private static void AssertRuntimeLightConverges(MojangDimension dimension, List<IChunk> lit, IChunk center)
    {
        var runtime = new RuntimeLightEngine(new System.Threading.Lock(), () => dimension.HasSkyLight);
        var originals = new Dictionary<IChunk, byte[]>();
        foreach (var chunk in lit)
        {
            originals[chunk] = Light(chunk);
            runtime.Track(chunk);
        }

        runtime.Flush();
        var position = new Vector((center.X << 4) + 15, dimension.MinY + dimension.Height / 2, (center.Z << 4) + 8);
        var originalBlock = center.GetBlock(position);
        center.SetBlock(position, Obsidian.Registries.BlocksRegistry.Glowstone);
        runtime.Flush();
        center.SetBlock(position, originalBlock);
        runtime.Flush();

        foreach (var chunk in lit)
            Assert.Equal(originals[chunk], Light(chunk));

        static byte[] Light(IChunk chunk) => chunk.Sections.ToArray()
            .SelectMany(section => section.SkyLightArray.ToArray().Concat(section.BlockLightArray.ToArray()))
            .ToArray();
    }

    private static MojangDimension Dimension(JsonElement entry) => entry.GetProperty("dimension").GetString() switch
    {
        "nether" => MojangDimension.Nether,
        "end" => MojangDimension.End,
        _ => MojangDimension.Overworld
    };

    private ChunkBuilder OverworldBuilder(JsonElement entry) =>
        worldgen.Builder(MojangDimension.Overworld, Seed(entry), structures: false);

    private static long Seed(JsonElement entry) => entry.GetProperty("seed").GetInt64();

    private static int Int(JsonElement entry, string name) => entry.GetProperty(name).GetInt32();

    private static string SeedName(JsonElement entry) => $"seed {Seed(entry)}";

    private static string PointName(JsonElement entry) =>
        $"{SeedName(entry)} ({Int(entry, "x")}, {Int(entry, "y")}, {Int(entry, "z")})";

    private static string QuartName(JsonElement entry) =>
        $"{SeedName(entry)} quart ({Int(entry, "quartX")}, {Int(entry, "quartY")}, {Int(entry, "quartZ")})";

    private static string ChunkName(JsonElement entry) =>
        $"{SeedName(entry)} chunk ({Int(entry, "chunkX")}, {Int(entry, "chunkZ")})";

    private static string DimensionChunkName(JsonElement entry) =>
        $"{entry.GetProperty("dimension").GetString()} {ChunkName(entry)}";

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
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    var block = chunk.GetBlock(x, y, z);
                    blocks.Append(block.IsAir ? "minecraft:air" : block.UnlocalizedName).Append('\n');
                }
            }
        }

        return blocks.ToString(0, blocks.Length - 1);
    }

    private static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

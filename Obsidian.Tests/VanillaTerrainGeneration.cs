using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Terrain;
using Obsidian.Tests.Utilities;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Replays isolated 26.3 terrain stages with structures disabled, supplying captured biome and carver inputs.
/// VanillaBiomeGeneration also replays the complete pipeline with the new biome source selecting those inputs.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
public class VanillaTerrainGeneration
{
    // Each terrain case replays every stage it captured, each stage on a fresh chunk.
    [Fact]
    public void TerrainMatchesVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var stages = VanillaWorldgenCaptures.CurrentCases("terrain").SelectMany(entry => Stages(entry, pipelines));

        FixtureCases.AssertAll(stages, stage => stage.Name, stage => stage.Check());
    }

    // Mask-only cases, for carver boundaries that need no whole terrain chunk.
    [Fact]
    public void CarvingMasksMatchVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var cases = VanillaWorldgenCaptures.CurrentCases("carvers");

        FixtureCases.AssertAll(cases, CaseName, entry => AssertCarvingMask(new Replay(entry, pipelines)));
    }

    // Standalone aquifer positions, each its own fluid sample.
    [Fact]
    public void AquifersMatchVanilla()
    {
        var pipelines = new WorldgenPipelines();
        var cases = VanillaWorldgenCaptures.CurrentCases("aquifers");

        FixtureCases.AssertAll(cases, AquiferName, entry => AssertAquifer(entry, pipelines));
    }

    private sealed record Stage(string Name, Action Check);

    private static IEnumerable<Stage> Stages(JsonElement entry, WorldgenPipelines pipelines)
    {
        var name = CaseName(entry);

        yield return new Stage($"{name} density", () => AssertDensity(entry, new Replay(entry, pipelines)));
        yield return new Stage($"{name} base fill", () => AssertBaseFill(entry, new Replay(entry, pipelines)));
        yield return new Stage($"{name} materials", () => AssertMaterials(entry, new Replay(entry, pipelines)));
        yield return new Stage($"{name} carving mask", () => AssertCarvingMask(new Replay(entry, pipelines)));

        if (entry.TryGetProperty("aquiferFluids", out var samples))
        {
            yield return new Stage(
                $"{name} aquifer fluids",
                () => AssertFluids(AquiferContext(entry, pipelines).Aquifer, samples.EnumerateArray()));
        }

        yield return new Stage($"{name} terrain", () => AssertTerrain(entry, new Replay(entry, pipelines)));
    }

    internal static string CaseName(JsonElement entry) =>
        $"{entry.GetProperty("seed")} {entry.GetProperty("dimension").GetString()} "
        + $"({entry.GetProperty("chunkX")}, {entry.GetProperty("chunkZ")})";

    private static string AquiferName(JsonElement entry) =>
        entry.TryGetProperty("name", out var name) ? name.GetString()! : CaseName(entry);

    private static void AssertDensity(JsonElement entry, Replay replay)
    {
        var volume = replay.Context.Volume;
        var expected = Decode(entry.GetProperty("densityData").GetString()!);
        var values = new float[volume.Size];

        replay.Generator.Router.FinalDensity.SampleVolume(values, volume);

        Assert.Equal(volume.Size, expected.Length);

        for (var index = 0; index < values.Length; index++)
        {
            var bits = BitConverter.SingleToInt32Bits(values[index]);
            Assert.True(expected[index] == bits,
                $"Density at buffer index {index}: expected 0x{expected[index]:X8}, actual 0x{bits:X8}");
        }
    }

    private static void AssertBaseFill(JsonElement entry, Replay replay)
    {
        replay.Generator.Fill(replay.Context);

        AssertBlocks(entry.GetProperty("base"), replay.Chunk);
        AssertHeightmaps(entry.GetProperty("baseHeightmaps"), replay.Chunk);
    }

    // The material rules over vanilla's base fill.
    private static void AssertMaterials(JsonElement entry, Replay replay)
    {
        RestoreBlocks(entry.GetProperty("base"), replay.Chunk);
        RestoreHeightmaps(entry.GetProperty("baseHeightmaps"), replay.Chunk);

        replay.Generator.Materials.Apply(replay.Context);

        AssertBlocks(entry.GetProperty("materials"), replay.Chunk);
    }

    private static void AssertCarvingMask(Replay replay)
    {
        var expected = Decode(replay.Entry.GetProperty("carvingMask").GetString()!);

        var mask = TerrainCarvers.GenerateMask(replay.Context, replay.CarversAt).Mask;

        var volume = replay.Context.Volume;

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = volume.MinBlockY; y < volume.MinBlockY + volume.SizeY; y++)
                {
                    var index = volume.Index(x, y - volume.MinBlockY, z);
                    var carved = y >= mask.MinY && y <= mask.MaxY && mask.Get(x, y, z);
                    Assert.True((expected[index] != 0) == carved,
                        $"Carving mask differs at ({x}, {y}, {z})");
                }
            }
        }
    }

    private static void AssertFluids(TerrainAquifer aquifer, IEnumerable<JsonElement> samples)
    {
        foreach (var sample in samples)
        {
            var fluid = aquifer.FluidAt(
                sample.GetProperty("x").GetInt32(),
                sample.GetProperty("y").GetInt32(),
                sample.GetProperty("z").GetInt32());

            Assert.Equal(sample.GetProperty("level").GetInt32(), fluid.FluidLevel);
            Assert.Equal(sample.GetProperty("fluid").GetString(), fluid.FluidType.UnlocalizedName);
        }
    }

    // A standalone position's fluid, and the block its density computes.
    private static void AssertAquifer(JsonElement entry, WorldgenPipelines pipelines)
    {
        AssertFluids(AquiferContext(entry, pipelines).Aquifer, [entry]);

        var aquifer = AquiferContext(entry, pipelines).Aquifer;

        var block = aquifer.ComputeSubstance(
            entry.GetProperty("x").GetInt32(),
            entry.GetProperty("y").GetInt32(),
            entry.GetProperty("z").GetInt32(),
            entry.GetProperty("density").GetDouble());

        Assert.Equal(entry.GetProperty("scheduled").GetBoolean(), aquifer.ShouldScheduleFluidUpdate);

        if (entry.GetProperty("block").ValueKind == JsonValueKind.Null)
        {
            Assert.Null(block);
            return;
        }

        var expected = ParseBlock(entry.GetProperty("block").GetString()!);
        Assert.NotNull(block);
        Assert.Equal(expected.RegistryId, block.RegistryId);
        Assert.Equal(expected.State?.Id ?? expected.DefaultId, block.State?.Id ?? block.DefaultId);
    }

    /// <summary>
    /// The terrain context of an aquifer case's chunk. A terrain case names its chunk, and a standalone position uses
    /// the chunk containing it, like the probe. Aquifers never look up biomes.
    /// </summary>
    private static TerrainContext AquiferContext(JsonElement entry, WorldgenPipelines pipelines)
    {
        var seed = entry.GetProperty("seed").GetInt64();
        var settings = entry.GetProperty("settings").GetString()!;
        var minY = entry.GetProperty("minY").GetInt32();
        var height = entry.GetProperty("height").GetInt32();
        var chunkX = entry.TryGetProperty("chunkX", out var x) ? x.GetInt32() : entry.GetProperty("x").GetInt32() >> 4;
        var chunkZ = entry.TryGetProperty("chunkZ", out var z) ? z.GetInt32() : entry.GetProperty("z").GetInt32() >> 4;
        var generator = pipelines.Terrain(seed, settings);
        var chunk = new Chunk(chunkX, chunkZ, minY, height);

        return generator.CreateContext(
            chunk,
            (_, _, _) => throw new InvalidOperationException("Aquifers use no biome."));
    }

    // The whole terrain status: biomes and carvers supplied, the blocks, heightmaps and post-processing marks.
    private static void AssertTerrain(JsonElement entry, Replay replay)
    {
        var updates = new List<Vector>();

        replay.Generator.Generate(replay.Chunk, replay.Biomes, replay.CarversAt, updates);

        AssertBlocks(entry.GetProperty("blocks"), replay.Chunk);
        AssertHeightmaps(entry.GetProperty("heightmaps"), replay.Chunk);

        var expectedUpdates = entry.GetProperty("postProcessing").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        Assert.Equal(expectedUpdates, PostProcessingIndices(updates, replay.Chunk));
    }

    // Post-processing marks by section, as their block indices in the chunk, like vanilla saves them.
    internal static int[] PostProcessingIndices(List<Vector> updates, Chunk chunk) => updates
        .OrderBy(position => (position.Y - chunk.MinY) >> 4)
        .Select(position => (position.Y - chunk.MinY) * 256 + (position.Z & 15) * 16 + (position.X & 15))
        .ToArray();

    private sealed class Replay
    {
        // Mask-only carver cases capture no block biomes.
        private readonly BlockBiomes? surfaceBiomes;
        private readonly BlockBiomes? carvingBiomes;
        private readonly WorldgenPipelines pipelines;
        private readonly string[][] carvers;

        public Replay(JsonElement entry, WorldgenPipelines pipelines)
        {
            var seed = entry.GetProperty("seed").GetInt64();
            var x = entry.GetProperty("chunkX").GetInt32();
            var z = entry.GetProperty("chunkZ").GetInt32();
            var minY = entry.GetProperty("minY").GetInt32();
            var height = entry.GetProperty("height").GetInt32();
            var settings = entry.GetProperty("settings").GetString()!;
            this.Entry = entry;
            this.Generator = pipelines.Terrain(seed, settings);
            this.Chunk = new Chunk(x, z, minY, height);

            if (entry.TryGetProperty("materialBiomes", out var surface))
                this.surfaceBiomes = new BlockBiomes(surface);

            if (entry.TryGetProperty("carvingBiomes", out var carving))
                this.carvingBiomes = new BlockBiomes(carving);

            this.pipelines = pipelines;

            this.carvers = entry.GetProperty("carverSources").EnumerateArray()
                .Select(names => names.EnumerateArray().Select(name => name.GetString()!).ToArray())
                .ToArray();
        }

        public JsonElement Entry { get; }

        public TerrainGenerator Generator { get; }

        public Chunk Chunk { get; }

        public TerrainContext Context => field ??= this.Generator.CreateContext(this.Chunk, this.Biomes);

        /// <summary>The surface's captured biomes, and carving's.</summary>
        public TerrainBiomes Biomes => new(this.SurfaceBiomeAt, this.CarvingBiomeAt);

        private string SurfaceBiomeAt(int x, int y, int z)
        {
            var biomes = this.surfaceBiomes ?? throw new InvalidOperationException("This case captured no block biomes.");

            return biomes.At(this.Chunk, x, y, z);
        }

        // Without captured carving biomes, carving reads fresh lookups, as vanilla's carvers do.
        private string CarvingBiomeAt(int x, int y, int z)
        {
            if (this.carvingBiomes is BlockBiomes captured)
                return captured.At(this.Chunk, x, y, z);

            var dimension = this.Entry.GetProperty("dimension").GetString()!;
            var seed = this.Entry.GetProperty("seed").GetInt64();

            return this.pipelines.Biomes(dimension, seed).BiomeAt(x, y, z);
        }

        public IReadOnlyList<string> CarversAt(int x, int z) =>
            this.carvers[(x - this.Chunk.X + 8) * 17 + z - this.Chunk.Z + 8];
    }

    /// <summary>A captured buffer of a chunk's block biomes, in z, x, y order with one extra y at the top.</summary>
    private sealed class BlockBiomes(JsonElement captured)
    {
        private readonly string[] palette = captured.GetProperty("palette").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        private readonly int[] indices = Decode(captured.GetProperty("data").GetString()!);

        public string At(Chunk chunk, int x, int y, int z)
        {
            var index = ((z & 15) * 16 + (x & 15)) * (chunk.Height + 1) + y - chunk.MinY;

            return this.palette[this.indices[index]];
        }
    }

    internal static void AssertBlocks(JsonElement expected, Chunk chunk)
    {
        var names = expected.GetProperty("palette").EnumerateArray().Select(value => value.GetString()!).ToArray();
        var palette = names.Select(ParseBlock).ToArray();
        var indices = Decode(expected.GetProperty("data").GetString()!);
        var index = 0;
        var mismatches = new List<string>();

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
                {
                    var expectedBlock = palette[indices[index]];
                    var actual = chunk.GetBlock(x, y, z);

                    if (actual.RegistryId != expectedBlock.RegistryId || actual.State?.Id != expectedBlock.State?.Id)
                    {
                        mismatches.Add($"({(chunk.X << 4) + x}, {y}, {(chunk.Z << 4) + z}): "
                            + $"expected {names[indices[index]]}, "
                            + $"actual {actual.UnlocalizedName} state {actual.State?.Id}");
                    }

                    index++;
                }
            }
        }

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} blocks differ. First differences:\n{string.Join("\n", mismatches.Take(12))}");
    }

    internal static void RestoreBlocks(JsonElement captured, Chunk chunk)
    {
        var palette = captured.GetProperty("palette").EnumerateArray()
            .Select(value => ParseBlock(value.GetString()!))
            .ToArray();
        var indices = Decode(captured.GetProperty("data").GetString()!);
        var index = 0;

        for (var z = 0; z < 16; z++)
        {
            for (var x = 0; x < 16; x++)
            {
                for (var y = chunk.MinY; y < chunk.MinY + chunk.Height; y++)
                {
                    chunk.SetBlock(x, y, z, palette[indices[index]]);
                    index++;
                }
            }
        }
    }

    private static void RestoreHeightmaps(JsonElement captured, Chunk chunk)
    {
        foreach (var (name, type) in HeightmapTypes())
        {
            var values = captured.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();

            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    chunk.Heightmaps[type].Set(x, z, values[z * 16 + x]);
                }
            }
        }
    }

    internal static IBlock ParseBlock(string name)
    {
        var separator = name.IndexOf('{');

        if (separator < 0)
        {
            return BlocksRegistry.Get(name);
        }

        var properties = name[(separator + 1)..^1].Split(", ")
            .Select(value => value.Split('='))
            .ToDictionary(pair => pair[0], pair => pair[1]);

        return BlocksRegistry.GetFromSimpleState(new SimpleBlockState
        {
            Name = name[..separator],
            Properties = properties
        });
    }

    private static void AssertHeightmaps(JsonElement expected, Chunk chunk)
    {
        foreach (var (name, type) in HeightmapTypes())
        {
            var values = expected.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray();

            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                {
                    Assert.Equal(values[z * 16 + x], chunk.Heightmaps[type].GetHeight(x, z));
                }
            }
        }
    }

    private static (string Name, HeightmapType Type)[] HeightmapTypes() =>
    [
        ("WORLD_SURFACE_WG", HeightmapType.WorldSurfaceWG),
        ("OCEAN_FLOOR_WG", HeightmapType.OceanFloorWG)
    ];

    internal static int[] Decode(string data)
    {
        using var source = new MemoryStream(Convert.FromBase64String(data));
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        var bytes = output.ToArray();
        var values = new int[bytes.Length / sizeof(int)];

        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(index * sizeof(int), sizeof(int)));
        }

        return values;
    }
}

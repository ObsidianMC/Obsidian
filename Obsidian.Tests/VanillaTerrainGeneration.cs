using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Terrain;
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
/// Replays 26.3 terrain status captures with structures disabled. Biome selection belongs to Phase 5 step 4:
/// these tests supply vanilla's captured biome and carver-list inputs through terrain's dependency hooks.
/// </summary>
[Trait("Category", TestCategory.Worldgen)]
[Collection(WorldgenCollection.Name)]
public class VanillaTerrainGeneration
{
    public sealed record TerrainCase(JsonElement Entry)
    {
        public override string ToString() =>
            $"{this.Entry.GetProperty("dimension").GetString()} "
            + $"({this.Entry.GetProperty("chunkX")}, {this.Entry.GetProperty("chunkZ")})";
    }

    public static TheoryData<TerrainCase> TerrainCases()
    {
        var cases = new TheoryData<TerrainCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("terrain"))
        {
            cases.Add(new TerrainCase(entry));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public void ChunkDensityMatchesVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);
        var volume = replay.Context.Volume;
        var expected = Decode(testCase.Entry.GetProperty("densityData").GetString()!);
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

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public void BaseFillMatchesVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);

        replay.Generator.Fill(replay.Context);

        AssertBlocks(testCase.Entry.GetProperty("base"), replay.Chunk);
        AssertHeightmaps(testCase.Entry.GetProperty("baseHeightmaps"), replay.Chunk);
    }

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public void MaterialsMatchVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);
        RestoreBlocks(testCase.Entry.GetProperty("base"), replay.Chunk);
        RestoreHeightmaps(testCase.Entry.GetProperty("baseHeightmaps"), replay.Chunk);

        replay.Generator.Materials.Apply(replay.Context);

        AssertBlocks(testCase.Entry.GetProperty("materials"), replay.Chunk);
    }

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public void CarvingMaskMatchesVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);
        var expected = Decode(testCase.Entry.GetProperty("carvingMask").GetString()!);

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

    public static TheoryData<TerrainCase> AquiferCases()
    {
        var cases = new TheoryData<TerrainCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("terrain"))
        {
            if (entry.TryGetProperty("aquiferFluids", out _))
            {
                cases.Add(new TerrainCase(entry));
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(AquiferCases))]
    public void AquiferFluidLevelsMatchVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);

        foreach (var sample in testCase.Entry.GetProperty("aquiferFluids").EnumerateArray())
        {
            var fluid = replay.Context.Aquifer.FluidAt(
                sample.GetProperty("x").GetInt32(),
                sample.GetProperty("y").GetInt32(),
                sample.GetProperty("z").GetInt32());

            Assert.Equal(sample.GetProperty("level").GetInt32(), fluid.FluidLevel);
            Assert.Equal(sample.GetProperty("fluid").GetString(), fluid.FluidType.UnlocalizedName);
        }
    }

    [Theory]
    [MemberData(nameof(TerrainCases))]
    public void TerrainStatusMatchesVanilla(TerrainCase testCase)
    {
        var replay = new Replay(testCase.Entry);
        var updates = new List<Vector>();

        replay.Generator.Generate(replay.Chunk, replay.BiomeAt, replay.CarversAt, updates);

        AssertBlocks(testCase.Entry.GetProperty("blocks"), replay.Chunk);
        AssertHeightmaps(testCase.Entry.GetProperty("heightmaps"), replay.Chunk);
        var expectedUpdates = testCase.Entry.GetProperty("postProcessing").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        var actualUpdates = updates.OrderBy(position => (position.Y - replay.Chunk.MinY) >> 4)
            .Select(position => (position.Y - replay.Chunk.MinY) * 256 + (position.Z & 15) * 16 + (position.X & 15))
            .ToArray();
        Assert.Equal(expectedUpdates, actualUpdates);
    }

    private sealed class Replay
    {
        private readonly string[] biomes;
        private readonly int[] biomeIndices;
        private readonly string[][] carvers;

        public Replay(JsonElement entry)
        {
            var seed = entry.GetProperty("seed").GetInt64();
            var x = entry.GetProperty("chunkX").GetInt32();
            var z = entry.GetProperty("chunkZ").GetInt32();
            var minY = entry.GetProperty("minY").GetInt32();
            var height = entry.GetProperty("height").GetInt32();
            this.Generator = new TerrainGenerator(WorldgenData.Vanilla, seed, entry.GetProperty("settings").GetString()!);
            this.Chunk = new Chunk(x, z, minY, height);
            var biomes = entry.GetProperty("materialBiomes");
            this.biomes = biomes.GetProperty("palette").EnumerateArray().Select(value => value.GetString()!).ToArray();
            this.biomeIndices = Decode(biomes.GetProperty("data").GetString()!);
            this.carvers = entry.GetProperty("carverSources").EnumerateArray()
                .Select(names => names.EnumerateArray().Select(name => name.GetString()!).ToArray())
                .ToArray();
        }

        public TerrainGenerator Generator { get; }

        public Chunk Chunk { get; }

        public TerrainContext Context => field ??= this.Generator.CreateContext(this.Chunk, this.BiomeAt);

        public string BiomeAt(int x, int y, int z)
        {
            var index = ((z & 15) * 16 + (x & 15)) * (this.Chunk.Height + 1) + y - this.Chunk.MinY;
            return this.biomes[this.biomeIndices[index]];
        }

        public IReadOnlyList<string> CarversAt(int x, int z) =>
            this.carvers[(x - this.Chunk.X + 8) * 17 + z - this.Chunk.Z + 8];
    }

    private static void AssertBlocks(JsonElement expected, Chunk chunk)
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

    private static void RestoreBlocks(JsonElement captured, Chunk chunk)
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

    private static int[] Decode(string data)
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

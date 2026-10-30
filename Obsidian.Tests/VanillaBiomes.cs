using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>Small replays of vanilla's 26.3 climate search and natural-spawn attribute composition.</summary>
public sealed class VanillaBiomes
{
    public sealed record CaptureCase(string Name, JsonElement Entry)
    {
        public override string ToString() => this.Name;
    }

    public static TheoryData<CaptureCase> SearchCases() => Cases("biome_search");

    public static TheoryData<CaptureCase> SpawnCases() => Cases("spawn_attributes");

    public static TheoryData<CaptureCase> PointCases() => Cases("biome_points");

    private static TheoryData<CaptureCase> Cases(string section)
    {
        var cases = new TheoryData<CaptureCase>();
        var index = 0;

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases(section))
        {
            var name = entry.TryGetProperty("name", out var label) ? label.GetString()! : section;

            if (entry.TryGetProperty("preset", out var preset))
            {
                name = preset.GetString()!;
            }

            if (entry.TryGetProperty("biome", out var biome))
            {
                name += "-" + biome.GetString();
            }

            cases.Add(new CaptureCase($"{index}-{name}", entry));
            index++;
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(SearchCases))]
    public void ParameterSearchMatchesVanilla(CaptureCase testCase)
    {
        var entry = testCase.Entry;
        IReadOnlyList<(ParameterPoint Point, int Value)> entries;

        if (entry.TryGetProperty("preset", out var preset))
        {
            var parameters = BiomeParameters.Read(WorldgenData.Vanilla, "minecraft:" + preset.GetString());
            entries = parameters.Entries.Select((value, index) => (value.Point, index)).ToArray();
        }
        else
        {
            entries = entry.GetProperty("parameters").EnumerateArray()
                .Select(value => (BiomeParameters.ReadPoint(value.GetProperty("parameters")),
                    value.GetProperty("value").GetInt32())).ToArray();
        }

        var tree = new BiomeParameterTree<int>(entries, childrenPerNode: 19);
        var targets = Decode(entry.GetProperty("targetsData").GetString()!);
        var expected = Decode(entry.GetProperty("resultsData").GetString()!);
        var actual = new int[expected.Length];

        for (var index = 0; index < actual.Length; index++)
        {
            var offset = index * 6;
            var target = new TargetPoint(targets[offset], targets[offset + 1], targets[offset + 2],
                targets[offset + 3], targets[offset + 4], targets[offset + 5]);
            actual[index] = tree.Search(target);
        }

        Assert.Equal(expected.Length * 6, targets.Length);
        Assert.Equal(entry.GetProperty("entryCount").GetInt32(), entries.Count);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PointCases))]
    public void ClimateAndPointBiomesMatchVanilla(CaptureCase testCase)
    {
        var entry = testCase.Entry;
        var dimension = entry.GetProperty("dimension").GetString()!;
        var preset = dimension == "minecraft:the_nether" ? "minecraft:nether" : dimension;
        var settings = NoiseGeneratorSettings.Read(WorldgenData.Vanilla, preset);

        if (entry.TryGetProperty("erosion", out var erosion))
        {
            var routerJson = JsonNode.Parse(settings.Router.GetRawText())!;
            routerJson["erosion"] = erosion.GetSingle();
            settings = settings with { Router = JsonSerializer.SerializeToElement(routerJson) };
        }

        var compiler = new DensityCompiler(WorldgenData.Vanilla, entry.GetProperty("seed").GetInt64(),
            settings.LegacyRandomSource);
        var router = DensityRouter.Create(settings, compiler);
        var source = new ClimateSource(WorldgenData.Vanilla, router, preset);
        var x = entry.GetProperty("quartX").GetInt32();
        var y = entry.GetProperty("quartY").GetInt32();
        var z = entry.GetProperty("quartZ").GetInt32();

        var climate = source.Sample(x, y, z);
        var biome = source.GetNoiseBiome(x, y, z);

        var expected = entry.GetProperty("climate").EnumerateArray().Select(value => value.GetInt64()).ToArray();
        Assert.Equal(expected, climate.ToParameterArray());
        Assert.Equal(entry.GetProperty("biome").GetString(), biome.Name);
    }

    [Theory]
    [MemberData(nameof(SpawnCases))]
    public void SpawnAttributesMatchVanilla(CaptureCase testCase)
    {
        var entry = testCase.Entry;
        var dimensionName = entry.GetProperty("dimension").GetString()!;
        var biomeName = entry.GetProperty("biome").GetString()!;
        var dimension = CodecRegistry.Dimensions.All[dimensionName];
        var biome = CodecRegistry.Biomes.All[biomeName];

        // Fresh elements also test the attachment path used by data-pack/plugin codecs, without mutating globals.
        dimension = dimension with { Element = dimension.Element with { } };
        biome = biome with { Element = biome.Element with { } };

        if (entry.TryGetProperty("dimensionAttributes", out var dimensionAttributes))
        {
            LevelEnvironment.Attach(dimension.Element,
                EnvironmentAttributes.Read(dimensionAttributes, "dimension", onlyPositional: false));
        }

        if (entry.TryGetProperty("biomeAttributes", out var biomeAttributes))
        {
            LevelEnvironment.Attach(biome.Element,
                EnvironmentAttributes.Read(biomeAttributes, "biome", onlyPositional: true));
        }

        var actual = BiomeSpawns.Vanilla.At(dimension, biome);

        AssertSpawnSettings(entry, actual);
    }

    [Fact]
    public void PluginModifiersApplyAfterDimensionAndBiomeAttributes()
    {
        var entry = VanillaWorldgenCaptures.CurrentCases("spawn_attributes")
            .Single(value => value.TryGetProperty("name", out var name) && name.GetString() == "clear-category");
        var dimension = CodecRegistry.Dimensions.Overworld;
        dimension = dimension with { Element = dimension.Element with { } };
        var biome = CodecRegistry.Biomes.Plains;
        biome = biome with { Element = biome.Element with { } };
        LevelEnvironment.Attach(dimension.Element,
            EnvironmentAttributes.Read(entry.GetProperty("dimensionAttributes"), "dimension", onlyPositional: false));
        LevelEnvironment.Attach(biome.Element, EnvironmentAttributeMap.Empty);
        var modifier = PluginFeatures.Parse(PluginFeatures.BiomeModifier, "biometests:remove_zombies", "test",
            Encoding.UTF8.GetBytes("""
                { "biomes": "minecraft:plains", "remove": { "spawners": ["minecraft:zombie"] } }
                """));
        var modified = PluginSpawns.Build([modifier], [], NullLogger.Instance);

        var actual = modified.At(dimension, biome);

        // Removing this category's sole entry matches vanilla's explicitly empty category overlay. Its inherited
        // creature list and spawn cost must survive. A fresh plugin snapshot restores the captured base settings.
        AssertSpawnSettings(entry, actual);
        var inherited = VanillaWorldgenCaptures.CurrentCases("spawn_attributes")
            .Single(value => value.TryGetProperty("name", out var name) && name.GetString() == "inherit");
        AssertSpawnSettings(inherited, PluginSpawns.Build([], [], NullLogger.Instance).At(dimension, biome));
    }

    internal static void AssertSpawnSettings(JsonElement entry, SpawnSettings actual)
    {
        var (expectedSpawns, expectedCosts) = BiomeData.ReadSpawnSettings(entry.GetProperty("spawns"), "capture");
        Assert.Equal(expectedSpawns.Keys.Order(), actual.Categories.Keys.Order());
        Assert.Equal(expectedCosts.Keys.Order(), actual.Costs.Keys.Order());
        Assert.Equal(entry.GetProperty("probability").GetSingle(), actual.CreatureProbability);

        foreach (var (category, spawners) in expectedSpawns)
        {
            var mobs = actual.Categories[category];
            Assert.Equal(spawners.Length, mobs.Length);

            for (var index = 0; index < mobs.Length; index++)
            {
                var expected = spawners[index];
                var mob = mobs[index];
                Assert.Equal(expected.Type, mob.Type);
                Assert.Equal(expected.Weight, mob.Weight);
                Assert.NotNull(mob.Count);
                Assert.Equal(IntProviderCodec.ToNbt(expected.Count!, "count").PrettyString(),
                    IntProviderCodec.ToNbt(mob.Count!, "count").PrettyString());
            }
        }

        foreach (var (type, cost) in expectedCosts)
        {
            Assert.Equal(cost.Charge, actual.Costs[type].Charge);
            Assert.Equal(cost.EnergyBudget, actual.Costs[type].EnergyBudget);
        }
    }

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

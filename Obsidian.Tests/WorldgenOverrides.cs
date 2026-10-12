using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Features;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Data packs' and plugins' files overriding vanilla's entries, like vanilla's data packs. The golden chunk tests check
/// that worlds without overrides generate as before.
/// </summary>
public sealed class WorldgenOverrides : IDisposable
{
    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-data-packs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact(DisplayName = "A data pack overriding the overworld's parameter list generates the nether's biomes there")]
    public void DataPackOverridesTheOverworldsParameterList()
    {
        var pack = Path.Join(this.folder, "nether_overworld");
        var list = Path.Join(pack, "data", "minecraft", "worldgen", "multi_noise_biome_source_parameter_list");
        Directory.CreateDirectory(list);
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), """{ "pack": { "pack_format": 94, "description": "" } }""");
        File.WriteAllText(Path.Join(list, "overworld.json"), """{ "preset": "minecraft:nether" }""");

        // Not a pack: no pack.mcmeta.
        Directory.CreateDirectory(Path.Join(this.folder, "notes"));

        var logger = new PluginFeatureFiles.ListLogger();
        var opened = Assert.Single(DataPack.OpenAll(this.folder, logger));
        var files = PluginNoise.FindFiles(opened.Files.Paths)
            .Select(file => PluginNoise.Parse(file.Registry, file.Identifier, file.Path, opened.Files.Read(file.Path)!))
            .ToList();

        var noise = PluginNoise.Build(files, [], logger);

        // Without overrides, the overworld keeps its compiled dimension.
        Assert.Null(PluginDimensions.ReadVanilla("minecraft:overworld", WorldgenNoise.Vanilla));

        var overworld = PluginDimensions.ReadVanilla("minecraft:overworld", noise)!.Dimension;
        Assert.Equal(MultiNoiseBiomeSource.NetherBiomes, overworld.PossibleBiomes);
        Assert.Same(MojangDimension.Overworld.Settings, overworld.Settings);

        // Without features, so the chunk is only the terrain and biomes.
        var noFeatures = new WorldgenFeatureSet(
            FrozenDictionary<string, ConfiguredFeatureBase>.Empty,
            FrozenDictionary<string, PlacedFeature>.Empty,
            FrozenDictionary<string, IReadOnlyList<IReadOnlyList<PlacedFeature>>>.Empty,
            FrozenDictionary<PlacedFeature, ImmutableArray<PluginContainer>>.Empty);

        var builder = new ChunkBuilder(overworld, 1234, generateStructures: false, () => noFeatures);
        var chunk = new FullChunks(builder, overworld).Get(0, 0);

        Assert.Contains(chunk.GetBiome(0, 64, 0), MultiNoiseBiomeSource.NetherBiomes);
        Assert.Single(logger.Messages, message => message.Contains("notes"));
    }

    [Fact(DisplayName = "Overriding a vanilla configured feature changes the vanilla placed features biomes place")]
    public void OverridesReachVanillaFeaturesNamingThem()
    {
        const string emeraldOre = """
            {
              "type": "minecraft:ore",
              "config": {
                "size": 4,
                "discard_chance_on_air_exposure": 0,
                "targets": [
                  {
                    "target": { "predicate_type": "minecraft:tag_match", "tag": "minecraft:stone_ore_replaceables" },
                    "state": { "Name": "minecraft:emerald_block" }
                  }
                ]
              }
            }
            """;

        var logger = new PluginFeatureFiles.ListLogger();
        var file = PluginFeatures.Parse(
            PluginFeatures.ConfiguredFeature,
            "minecraft:ore_diamond_small",
            "test",
            Encoding.UTF8.GetBytes(emeraldOre));

        var features = PluginFeatures.Build([file], [], [], logger, [MojangDimension.Overworld]);
        Assert.Empty(logger.Messages);

        var placed = features.BiomeFeatures["minecraft:plains"]
            .SelectMany(step => step)
            .Single(feature => feature.Identifier == "minecraft:ore_diamond");

        // The vanilla placed feature naming it is read again around the override; unrelated ones stay compiled.
        Assert.NotSame(PlacedFeatures.All["minecraft:ore_diamond"], placed);
        Assert.Same(features.ConfiguredFeatures["minecraft:ore_diamond_small"], placed.Feature);
        Assert.Contains(
            PlacedFeatures.All["minecraft:ore_coal_upper"],
            features.BiomeFeatures["minecraft:plains"].SelectMany(step => step));
    }

    [Fact(DisplayName = "An overridden vanilla synced entry is sent with its data, and by name once not overridden")]
    public void OverriddenVanillaEntriesAreSentWithData()
    {
        static CodecFile Biome(string temperature) => PluginCodecs.Parse(
            PluginCodecs.Biome,
            "minecraft:plains",
            "test",
            Encoding.UTF8.GetBytes($$"""
                {
                  "has_precipitation": true,
                  "temperature": {{temperature}},
                  "downfall": 0.4,
                  "effects": { "water_color": "#3f76e4" },
                  "carvers": [],
                  "features": [],
                  "spawners": {},
                  "spawn_costs": {}
                }
                """));

        // A table of its own, as the server's is shared with other tests.
        var vanilla = (BiomeElement)Biome("0.8").Element;
        var plains = new BiomeCodec { Name = "minecraft:plains", Id = 0, Element = vanilla };
        var table = new CodecTable<BiomeCodec>([plains]);
        var hot = Biome("2");

        var changed = table.Apply(
            [new PluginCodecEntry<BiomeElement>(hot.Identifier, (BiomeElement)hot.Element, hot.Json, hot.Data)],
            (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
            codec => codec.Element,
            (codec, element) => codec.Element = element);

        Assert.True(changed);
        Assert.True(table.IsOverridden("minecraft:plains"));
        Assert.Same(plains, table.All["minecraft:plains"]);
        Assert.Equal(2f, plains.Element.Temperature);
        Assert.Same(hot.Data, Assert.Single(table.NetworkEntries).Data);

        table.Apply<BiomeElement>(
            [],
            (name, id, element) => new BiomeCodec { Name = name, Id = id, Element = element },
            codec => codec.Element,
            (codec, element) => codec.Element = element);

        Assert.False(table.IsOverridden("minecraft:plains"));
        Assert.Same(vanilla, plains.Element);
        Assert.Null(Assert.Single(table.NetworkEntries).Data);
    }
}

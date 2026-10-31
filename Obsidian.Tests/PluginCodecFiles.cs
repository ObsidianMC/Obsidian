using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' biome, dimension type and damage type files, added to the codec registries like a data pack's entries.
/// </summary>
public partial class PluginCodecFiles
{
    private const string MinimalBiome = """
        {
          "has_precipitation": true,
          "temperature": 0.5,
          "downfall": 0.5,
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": []
        }
        """;

    private const string MinimalDimension = """
        {
          "has_skylight": true,
          "has_ceiling": false,
          "has_ender_dragon_fight": false,
          "coordinate_scale": 1,
          "min_y": -64,
          "height": 384,
          "logical_height": 384,
          "infiniburn": "#minecraft:infiniburn_overworld",
          "ambient_light": 0,
          "monster_spawn_light_level": 7,
          "monster_spawn_block_light_limit": 0
        }
        """;

    [Fact(DisplayName = "A plugin's biome registers after vanilla's, keeps its id on reload and is sent with data")]
    public void AddsPluginBiomes()
    {
        const string json = """
            {
              "has_precipitation": true,
              "temperature": 0.5,
              "downfall": 1,
              "attributes": {
                "minecraft:visual/sky_color": 7907327,
                "gameplay/can_start_raid": false,
                "gameplay/natural_mob_spawns": {
                  "modifier": "overlay",
                  "argument": {
                    "spawns_by_category": {
                      "monster": [{ "type": "minecraft:zombie", "weight": 100, "count": 4 }]
                    },
                    "spawn_costs": {}
                  }
                }
              },
              "effects": { "water_color": 4159204 },
              "carvers": ["minecraft:cave"],
              "features": [[], "minecraft:ore_coal_upper"]
            }
            """;

        var vanillaCount = CodecRegistry.Biomes.Table.VanillaCount;

        // Clients couldn't decode a dimension type without vanilla's fields, so such a file is left out.
        Assert.Throws<InvalidDataException>(
            () => PluginCodecs.Parse(PluginCodecs.DimensionType, "codecfiles:void", "test", "{}"u8));

        PluginCodecs.Apply([Parse("codecfiles:glade", json)], NullLogger.Instance);

        Assert.True(CodecRegistry.TryGetBiome("codecfiles:glade", out var glade));
        Assert.True(glade!.Id >= vanillaCount);
        Assert.Same(glade, CodecRegistry.GetBiome(glade.Id));
        Assert.Equal(0.5f, glade.Element.Temperature);

        // Reloading with other data keeps the entry and its id, and tells clients to configure again.
        var revision = PluginCodecs.Revision;
        PluginCodecs.Apply([Parse("codecfiles:glade", json.Replace("0.5", "0.25"))], NullLogger.Instance);

        Assert.Same(glade, CodecRegistry.Biomes.All["codecfiles:glade"]);
        Assert.Equal(0.25f, glade.Element.Temperature);
        Assert.True(PluginCodecs.Revision > revision);

        // Vanilla's biomes are sent by name, the plugin's as vanilla's network codec writes it.
        using var buffer = new NetworkBuffer();
        new RegistryDataPacket(CodecRegistry.Biomes.CodecKey, CodecRegistry.Biomes.Table.NetworkEntries)
            .Serialize(buffer);

        using var reader = new NetworkBuffer(buffer.GetBuffer()[..buffer.Size]);
        Assert.Equal(CodecRegistry.Biomes.CodecKey, reader.ReadString());

        var count = reader.ReadVarInt();
        NbtCompound? data = null;

        for (var id = 0; id < count; id++)
        {
            var name = reader.ReadString();
            var hasData = reader.ReadBoolean();
            Assert.Equal(id >= vanillaCount, hasData);

            if (!hasData)
                continue;

            var entry = reader.ReadNbtCompound();
            if (name == glade.Name)
            {
                Assert.Equal(glade.Id, id);
                data = entry;
            }
        }

        // Floats as float tags, booleans as bytes, colours as vanilla's hex strings, and only synced attributes.
        Assert.NotNull(data);
        Assert.Equal(0.25f, Assert.IsType<NbtTag<float>>(data["temperature"]).Value);
        Assert.Equal(1f, Assert.IsType<NbtTag<float>>(data["downfall"]).Value);
        Assert.Equal((byte)1, Assert.IsType<NbtTag<byte>>(data["has_precipitation"]).Value);
        Assert.Equal("#3f76e4", Assert.IsType<NbtCompound>(data["effects"]).GetString("water_color"));

        var attributes = Assert.IsType<NbtCompound>(data["attributes"]);
        Assert.Equal("#78a7ff", attributes.GetString("minecraft:visual/sky_color"));
        Assert.False(attributes.HasTag("minecraft:gameplay/can_start_raid"));
        Assert.False(data.HasTag("features"));
    }

    // Files vanilla rejects and alternative forms it accepts, each a minimal valid file with the row's JSON merged in.
    [Theory(DisplayName = "Plugins' files are accepted exactly where vanilla's codecs accept them")]
    [InlineData(PluginCodecs.DimensionType, false, """{ "height": 17, "logical_height": 16 }""")]
    [InlineData(PluginCodecs.Biome, true, """{ "effects": { "water_color": [0.1, 0.2, 0.3] } }""")]
    [InlineData(
        PluginCodecs.DimensionType,
        true,
        """{ "monster_spawn_light_level": { "type": "minecraft:constant", "value": 7 } }""")]
    [InlineData(
        PluginCodecs.Biome,
        true,
        """
        { "attributes": { "minecraft:visual/ambient_particles": [
        { "particle": { "type": "item", "item": { "id": "diamond" } }, "probability": 0.1 },
        { "particle": { "type": "block_crumble", "block_state": { "id": "stone" } }, "probability": 0.1 },
        { "particle": { "type": "geyser", "water_blocks": 3 }, "probability": 0.1 },
        { "particle": { "type": "geyser_plume", "water_blocks": 3 }, "probability": 0.1 },
        { "particle": { "type": "geyser_base", "water_blocks": 4, "burst_impulse_base": 1.5 }, "probability": 0.1 },
        { "particle": { "type": "geyser_poof", "water_blocks": 4, "burst_impulse_base": 1.5 }, "probability": 0.1 },
        { "particle": { "type": "trail", "target": [0, 64, 0], "color": [0, 1, 0], "duration": 20 },
        "probability": 0.1 }] } }
        """)]
    public void ValidatesLikeVanilla(string registry, bool valid, string patch)
    {
        var bytes = Patched(registry == PluginCodecs.Biome ? MinimalBiome : MinimalDimension, patch);

        if (valid)
            PluginCodecs.Parse(registry, "codecfiles:case", "test", bytes);
        else
            Assert.Throws<InvalidDataException>(() => PluginCodecs.Parse(registry, "codecfiles:case", "test", bytes));
    }

    [Fact]
    public void CodecDataUsesVanillaTagTypes()
    {
        {
            var patch = """{ "monster_spawn_light_level": { "type": "minecraft:constant", "value": 7 } }""";
            var file = PluginCodecs.Parse(
                PluginCodecs.DimensionType,
                "codecfiles:flat",
                "test",
                Patched(MinimalDimension, patch));

            Assert.Equal(7, Assert.IsType<NbtTag<int>>(file.Data["monster_spawn_light_level"]).Value);
            Assert.Equal(1.0, Assert.IsType<NbtTag<double>>(file.Data["coordinate_scale"]).Value);
            Assert.Equal((byte)1, Assert.IsType<NbtTag<byte>>(file.Data["has_skylight"]).Value);
            Assert.False(file.Data.HasTag("skybox"));
        }

        {
            var json = """
                {
                  "minecraft:visual/ambient_particles": [
                    { "particle": { "type": "dust", "color": [1, 0, 0], "scale": 0.5 }, "probability": 0.1 },
                    { "particle": { "type": "effect", "color": -1, "power": 1 }, "probability": 0.1 },
                    { "particle": { "type": "dragon_breath", "power": 1 }, "probability": 0.1 },
                    { "particle": { "type": "dragon_breath" }, "probability": 0.1 },
                    { "particle": { "type": "item", "item": { "id": "diamond", "count": 3 } }, "probability": 0.1 }
                  ]
                }
                """;

            using var document = JsonDocument.Parse(json);
            var map = EnvironmentAttributes.Read(document.RootElement, "attributes", onlyPositional: true);
            var particles = Assert.IsType<NbtList>(Assert.Single(map.Entries).Value);

            var dust = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(particles[0])["particle"]);
            Assert.Equal(unchecked((int)0xFFFF0000), Assert.IsType<NbtTag<int>>(dust["color"]).Value);
            Assert.Equal(0.5f, Assert.IsType<NbtTag<float>>(dust["scale"]).Value);

            var effect = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(particles[1])["particle"]);
            Assert.Equal(["type"], effect.Select(tag => tag.Key));

            // Vanilla compares dragon breath's default by reference: only an omitted power is left out.
            var breath = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(particles[2])["particle"]);
            Assert.Equal(1f, Assert.IsType<NbtTag<float>>(breath["power"]).Value);

            var defaultBreath = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(particles[3])["particle"]);
            Assert.Equal(["type"], defaultBreath.Select(tag => tag.Key));
            var item = Assert.IsType<NbtCompound>(Assert.IsType<NbtCompound>(particles[4])["particle"]);
            Assert.Equal(3, Assert.IsType<NbtTag<int>>(Assert.IsType<NbtCompound>(item["item"])["count"]).Value);
        }
    }

    [Fact(DisplayName = "A plugin's dimension type's light level provider samples with vanilla's draws")]
    public void SamplesLightLevelProvidersLikeVanilla()
    {
        var patch = """
            {
              "monster_spawn_light_level": {
                "type": "minecraft:weighted_list",
                "distribution": [
                  { "data": 0, "weight": 1 },
                  { "data": { "type": "minecraft:uniform", "min_inclusive": 5, "max_inclusive": 7 }, "weight": 1 }
                ]
              }
            }
            """;

        using var document = JsonDocument.Parse(Patched(MinimalDimension, patch));
        var provider = DimensionTypeData.Read(document.RootElement).ToElement().MonsterSpawnLightProvider;
        Assert.NotNull(provider);

        // Vanilla 1.21.11's WeightedListInt with RandomSource.create(42): nextInt(2) picks, the uniform draws again.
        var random = new LegacyRandomSource(42);
        int[] expected = [5, 7, 0, 7, 6, 0, 7, 0];

        Assert.Equal(expected, expected.Select(_ => provider.Sample(random)));
    }

    [Theory]
    [InlineData("""{"type":"minecraft:trapezoid","min":1,"max":5,"plateau":1}""",
        1, 5, new int[] { 3, 3, 3, 3, 3, 4, 2, 3 }, 56)]
    [InlineData("""{"type":"minecraft:trapezoid","min":-3,"max":3,"plateau":0}""",
        -3, 3, new int[] { 2, 2, -2, -1, 2, 2, 0, -1 }, 56)]
    [InlineData("""{"type":"minecraft:trapezoid","min":1,"max":5,"plateau":4}""",
        1, 5, new int[] { 1, 4, 4, 5, 1, 1, 1, 4 }, 19)]
    [InlineData("""{"type":"minecraft:trapezoid","min":2,"max":2,"plateau":0}""",
        2, 2, new int[] { 2, 2, 2, 2, 2, 2, 2, 2 }, 19)]
    [InlineData("""{"type":"minecraft:trapezoid","min":1,"max":5,"plateau":-1}""",
        1, 5, new int[] { 3, 3, 2, 5, 4, 5, 1, 5 }, 56)]
    [InlineData("""{"type":"minecraft:very_biased_to_bottom","min_inclusive":1,"max_inclusive":5}""",
        1, 5, new int[] { 1, 1, 1, 2, 1, 1, 4, 1 }, 43)]
    public void RoundTripsAndSamples26_3IntProviders(
        string json,
        int min,
        int max,
        int[] expected,
        int nextDraw)
    {
        using var document = JsonDocument.Parse(json);
        var random = new LegacyRandomSource(42);

        var provider = IntProviderCodec.Read(document.RootElement, "count");
        var encoded = Format121Data.ToJson(IntProviderCodec.ToNbt(provider, "count"));
        var samples = expected.Select(_ => provider.Sample(random)).ToArray();

        // Captured from the 26.3 providers with RandomSource.create(42), including the following draw.
        Assert.Equal(min, provider.MinValue);
        Assert.Equal(max, provider.MaxValue);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), encoded));
        Assert.Equal(expected, samples);
        Assert.Equal(nextDraw, random.NextInt(100));
    }

    [Fact]
    public void RejectsInvalid26_3IntProviders()
    {
        string[] cases =
        [
            """{"type":"minecraft:trapezoid","min":5,"max":1,"plateau":0}""",
            """{"type":"minecraft:trapezoid","min":1,"max":5,"plateau":5}""",
            """{"type":"minecraft:trapezoid","max":5,"plateau":1}""",
            """{"type":"minecraft:trapezoid","min":1,"plateau":1}""",
            """{"type":"minecraft:trapezoid","min":1,"max":5}""",
            """{"type":"minecraft:very_biased_to_bottom","min_inclusive":5,"max_inclusive":1}""",
            """{"type":"minecraft:very_biased_to_bottom","max_inclusive":5}"""
        ];

        FixtureCases.AssertAll(cases, row => $"json={row}", AssertRejectsInvalid26_3IntProviders);
    }

    private static void AssertRejectsInvalid26_3IntProviders(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<InvalidDataException>(() => IntProviderCodec.Read(document.RootElement, "count"));
    }

    [Fact(DisplayName = "The server applies a dimension type's gameplay attributes, and vanilla's nether's")]
    public void AppliesGameplayAttributes()
    {
        var patch = """
            {
              "attributes": {
                "minecraft:gameplay/can_start_raid": false,
                "minecraft:gameplay/piglins_zombify": { "modifier": "and", "argument": false }
              }
            }
            """;

        using var document = JsonDocument.Parse(Patched(MinimalDimension, patch));
        var type = new DimensionCodec
        {
            Name = "codecfiles:gameplay",
            Id = -1,
            Element = DimensionTypeData.Read(document.RootElement).ToElement()
        };

        Assert.False(type.Gameplay(LevelEnvironment.CanStartRaid));
        Assert.True(type.PiglinsSafe());
        Assert.False(FluidRules.ForDimension(type).FastLava);

        // The server's copy of vanilla's types has no attributes, so it knows the nether's.
        var nether = CodecRegistry.Dimensions.All["minecraft:the_nether"];
        Assert.False(nether.Gameplay(LevelEnvironment.CanStartRaid));
        Assert.True(nether.PiglinsSafe());
        Assert.True(FluidRules.ForDimension(nether).FastLava);
        Assert.True(CodecRegistry.Dimensions.All["minecraft:the_end"].Gameplay(LevelEnvironment.CanStartRaid));
    }

    // A file with a patch's fields merged in, objects into objects.
    private static byte[] Patched(string json, string patch)
    {
        var file = JsonNode.Parse(json)!.AsObject();
        Merge(file, JsonNode.Parse(patch)!.AsObject());

        return Encoding.UTF8.GetBytes(file.ToJsonString());

        static void Merge(JsonObject target, JsonObject patch)
        {
            foreach (var (name, value) in patch.ToList())
            {
                if (value is JsonObject inner && target[name] is JsonObject existing)
                    Merge(existing, inner);
                else
                    target[name] = value?.DeepClone();
            }
        }
    }

    private static CodecFile Parse(string identifier, string json) =>
        PluginCodecs.Parse(PluginCodecs.Biome, identifier, "test", Encoding.UTF8.GetBytes(json));
}

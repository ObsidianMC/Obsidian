using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' biome, dimension type and damage type files, added to the codec registries like a data pack's entries.
/// </summary>
public class PluginCodecFiles
{
    private const string MinimalBiome = """
        {
          "has_precipitation": true,
          "temperature": 0.5,
          "downfall": 0.5,
          "effects": { "water_color": "#3f76e4" },
          "carvers": [],
          "features": [],
          "spawners": {},
          "spawn_costs": {}
        }
        """;

    private const string MinimalDimension = """
        {
          "has_skylight": true,
          "has_ceiling": false,
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
              "attributes": { "minecraft:visual/sky_color": 7907327, "gameplay/can_start_raid": false },
              "effects": { "water_color": 4159204 },
              "spawners": {
                "monster": [{ "type": "minecraft:zombie", "weight": 100, "minCount": 1, "maxCount": 4 }]
              },
              "spawn_costs": {},
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
    [InlineData(PluginCodecs.Biome, false, """{ "attributes": 17 }""")]
    [InlineData(PluginCodecs.Biome, false, """{ "effects": { "water_color": {} } }""")]
    [InlineData(
        PluginCodecs.DimensionType,
        false,
        """{ "monster_spawn_light_level": { "type": "uniform", "min_inclusive": 0, "max_inclusive": 16 } }""")]
    [InlineData(PluginCodecs.Biome, true, """{ "effects": { "water_color": [0.1, 0.2, 0.3] } }""")]
    [InlineData(
        PluginCodecs.DimensionType,
        true,
        """{ "monster_spawn_light_level": { "type": "minecraft:constant", "value": 7 } }""")]
    public void ValidatesLikeVanilla(string registry, bool valid, string patch)
    {
        var bytes = Patched(registry == PluginCodecs.Biome ? MinimalBiome : MinimalDimension, patch);

        if (valid)
            PluginCodecs.Parse(registry, "codecfiles:case", "test", bytes);
        else
            Assert.Throws<InvalidDataException>(() => PluginCodecs.Parse(registry, "codecfiles:case", "test", bytes));
    }

    [Fact(DisplayName = "A plugin's dimension type is sent with vanilla's tag types, a constant provider as an int")]
    public void EncodesDimensionTypesLikeVanilla()
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

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
    [InlineData(
        PluginCodecs.Biome,
        false,
        """
        { "attributes": { "minecraft:visual/ambient_particles": [
        { "particle": { "type": "dust", "color": 255, "scale": 5 }, "probability": 0.1 }] } }
        """)]
    [InlineData(
        PluginCodecs.Biome,
        false,
        """
        { "attributes": { "minecraft:visual/ambient_particles": [
        { "particle": { "type": "block", "block_state": "codecfiles:missing" }, "probability": 0.1 }] } }
        """)]
    [InlineData(
        PluginCodecs.Biome,
        false,
        """
        { "attributes": { "minecraft:visual/default_dripstone_particle": {
        "type": "vibration", "destination": { "type": "entity", "source_entity": [0, 0, 0, 0] },
        "arrival_in_ticks": 5 } } }
        """)]
    [InlineData(
        PluginCodecs.Biome,
        true,
        """
        { "attributes": { "minecraft:visual/ambient_particles": [
        { "particle": { "type": "item", "item": { "id": "diamond" } }, "probability": 0.1 },
        { "particle": { "type": "block_crumble", "block_state": { "Name": "stone" } }, "probability": 0.1 },
        { "particle": { "type": "trail", "target": [0, 64, 0], "color": [0, 1, 0], "duration": 20 },
        "probability": 0.1 }] } }
        """)]
    [InlineData(
        PluginCodecs.DimensionType,
        false,
        """
        { "attributes": { "minecraft:gameplay/bed_rule": {
        "can_sleep": "never", "can_set_spawn": "never", "error_message": { "bold": true } } } }
        """)]
    [InlineData(
        PluginCodecs.DimensionType,
        true,
        """
        { "attributes": { "minecraft:gameplay/bed_rule": {
        "can_sleep": "never", "can_set_spawn": "never", "explodes": true, "error_message": {
        "translate": "codecfiles.too_hot", "with": [{ "text": "here", "bold": true }, 3], "extra": ["!"] } } } }
        """)]
    [InlineData(
        PluginCodecs.DimensionType,
        true,
        """
        { "attributes": { "minecraft:gameplay/bed_rule": {
        "can_sleep": "never", "can_set_spawn": "never", "error_message": ["Too hot ", { "sprite": "item/blaze_powder" },
        { "player": { "name": "Steve", "id": [1, 2, 3, 4] }, "hat": false },
        { "type": "object", "object": "player", "player": "Alex" }] } } }
        """)]
    [InlineData(
        PluginCodecs.DimensionType,
        false,
        """
        { "attributes": { "minecraft:gameplay/bed_rule": {
        "can_sleep": "never", "can_set_spawn": "never", "error_message": { "player": "not a player name" } } } }
        """)]
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

    [Fact(DisplayName = "Particle options are sent as vanilla's codecs write them, defaults left out")]
    public void EncodesParticleOptionsLikeVanilla()
    {
        var json = """
            {
              "minecraft:visual/ambient_particles": [
                { "particle": { "type": "dust", "color": [1, 0, 0], "scale": 0.5 }, "probability": 0.1 },
                { "particle": { "type": "effect", "color": -1, "power": 1 }, "probability": 0.1 }
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

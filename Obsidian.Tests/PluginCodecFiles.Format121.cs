using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

public partial class PluginCodecFiles
{
    // These assets retain vanilla's complete datagen JSON, before the codec source generator selects its fields.
    [Theory]
    [InlineData(PluginCodecs.Biome, "biome", 67)]
    [InlineData(PluginCodecs.DimensionType, "dimension_type", 4)]
    public void VanillaFormat121RegistryFilesRoundTrip(string registry, string asset, int count)
    {
        using var document = Format121Data.Read(asset);
        var files = document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(count, files.Length);

        foreach (var file in files)
        {
            var parsed = PluginCodecs.Parse(
                registry,
                file.Name,
                file.Name,
                Encoding.UTF8.GetBytes(file.Value.GetRawText()));
            var roundTrip = JsonNode.Parse(file.Value.GetRawText())!.AsObject();
            EnvironmentAttributeMap attributes;

            if (registry == PluginCodecs.Biome)
            {
                var biome = BiomeData.Read(file.Value);
                var element = Assert.IsType<BiomeElement>(parsed.Element);
                attributes = biome.Attributes;

                Assert.Equal(file.Value.GetProperty("temperature").GetSingle(), element.Temperature);
                Assert.Equal(file.Value.GetProperty("downfall").GetSingle(), element.Downfall);
                Assert.Equal(file.Value.GetProperty("has_precipitation").GetBoolean(), element.HasPrecipitation);
                Assert.Equal(element.Temperature, Assert.IsType<NbtTag<float>>(parsed.Data["temperature"]).Value);
                Assert.Equal(element.Downfall, Assert.IsType<NbtTag<float>>(parsed.Data["downfall"]).Value);
                Assert.Equal(
                    element.HasPrecipitation,
                    Assert.IsType<NbtTag<byte>>(parsed.Data["has_precipitation"]).Value != 0);
                AssertVanillaSpawns(file.Value, element);
            }
            else
            {
                var dimension = DimensionTypeData.Read(file.Value);
                var element = Assert.IsType<DimensionElement>(parsed.Element);
                attributes = dimension.Attributes;

                var hasClock = file.Value.TryGetProperty("default_clock", out var clock);
                Assert.Equal(hasClock, parsed.Data.HasTag("default_clock"));

                if (hasClock)
                {
                    Assert.Equal(clock.GetString(), element.DefaultClock);
                    Assert.Equal(element.DefaultClock, parsed.Data.GetString("default_clock"));
                }
                else
                {
                    Assert.Null(element.DefaultClock);
                }

                Assert.Equal(
                    file.Value.GetProperty("has_ender_dragon_fight").GetBoolean(),
                    element.HasEnderDragonFight);
                Assert.Equal(
                    element.HasEnderDragonFight,
                    Assert.IsType<NbtTag<byte>>(parsed.Data["has_ender_dragon_fight"]).Value != 0);
                Assert.Equal(file.Value.GetProperty("min_y").GetInt32(), element.MinY);
                Assert.Equal(file.Value.GetProperty("height").GetInt32(), element.Height);
            }

            var network = Format121Data.ToJson(parsed.Data)!.AsObject();

            foreach (var (name, value) in network)
            {
                roundTrip[name] = value?.DeepClone();
            }

            // The network codec omits server-only attributes. Round-trip the complete attribute codec separately.
            var complete = new JsonObject();

            foreach (var entry in attributes.Entries)
            {
                complete[entry.Attribute] = Format121Data.ToJson(entry.Value);
                var source = file.Value.GetProperty("attributes").GetProperty(entry.Attribute);
                Assert.True(
                    JsonNode.DeepEquals(JsonNode.Parse(source.GetRawText()), complete[entry.Attribute]),
                    $"{file.Name}: {entry.Attribute}");

                var synced = network["attributes"]?.AsObject();
                Assert.Equal(entry.Syncable, synced?.ContainsKey(entry.Attribute) == true);
            }

            var originalAttributes = file.Value.GetProperty("attributes").EnumerateObject();
            Assert.Equal(
                originalAttributes.Select(property => property.Name).Order(),
                complete.Select(entry => entry.Key).Order());
            roundTrip["attributes"] = complete;

            var reread = PluginCodecs.Parse(
                registry,
                file.Name,
                "round trip",
                Encoding.UTF8.GetBytes(roundTrip.ToJsonString()));

            Assert.True(JsonNode.DeepEquals(network, Format121Data.ToJson(reread.Data)), file.Name);

            if (reread.Element is BiomeElement rereadBiome)
            {
                AssertVanillaSpawns(file.Value, rereadBiome);
            }
        }
    }

    private static void AssertVanillaSpawns(JsonElement root, BiomeElement biome)
    {
        var attributes = root.GetProperty("attributes");
        if (!attributes.TryGetProperty("minecraft:gameplay/natural_mob_spawns", out var spawns))
        {
            Assert.Empty(biome.Spawners);
            Assert.Empty(biome.SpawnCosts);
            return;
        }

        var settings = spawns.GetProperty("argument");
        var categories = settings.GetProperty("spawns_by_category").EnumerateObject().ToArray();
        Assert.Equal(categories.Select(category => category.Name).Order(), biome.Spawners.Keys.Order());

        foreach (var category in categories)
        {
            var expected = category.Value.EnumerateArray().ToArray();
            var actual = biome.Spawners[category.Name];
            Assert.Equal(expected.Length, actual.Length);

            for (var index = 0; index < expected.Length; index++)
            {
                Assert.Equal(expected[index].GetProperty("type").GetString(), actual[index].Type);
                Assert.Equal(expected[index].GetProperty("weight").GetInt32(), actual[index].Weight);
                Assert.NotNull(actual[index].Count);

                var count = IntProviderCodec.ToNbt(actual[index].Count!, "count");
                var sourceCount = JsonNode.Parse(expected[index].GetProperty("count").GetRawText());
                Assert.True(JsonNode.DeepEquals(sourceCount, Format121Data.ToJson(count)));
            }
        }

        var costs = settings.GetProperty("spawn_costs").EnumerateObject().ToArray();
        Assert.Equal(costs.Select(cost => cost.Name).Order(), biome.SpawnCosts.Keys.Order());

        foreach (var cost in costs)
        {
            Assert.Equal(cost.Value.GetProperty("charge").GetDouble(), biome.SpawnCosts[cost.Name].Charge);
            Assert.Equal(cost.Value.GetProperty("energy_budget").GetDouble(), biome.SpawnCosts[cost.Name].EnergyBudget);
        }

        var probability = 0.1f;
        if (attributes.TryGetProperty("minecraft:gameplay/creature_world_gen_spawn_probability", out var value))
        {
            probability = value.GetSingle();
        }

        Assert.Equal(probability, biome.CreatureSpawnProbability);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsNaturalSpawnsAsValuesOrModifiers(bool direct)
    {
        using var document = Format121Data.Read("biome");
        var original = document.RootElement.GetProperty("minecraft:plains");
        var root = JsonNode.Parse(original.GetRawText())!.AsObject();
        var attributes = root["attributes"]!.AsObject();
        var spawns = attributes["minecraft:gameplay/natural_mob_spawns"]!.AsObject();

        if (direct)
        {
            var value = spawns["argument"]!.DeepClone().AsObject();
            value["modifier"] = "unknown_field";
            value["argument"] = 0;
            attributes["minecraft:gameplay/natural_mob_spawns"] = value;
        }

        using var changed = JsonDocument.Parse(root.ToJsonString());
        var parsed = BiomeData.Read(changed.RootElement).ToElement();

        AssertVanillaSpawns(original, parsed);
    }

    [Theory]
    [InlineData("default_clock", "\"missing:clock\"")]
    [InlineData("default_clock", "\"INVALID\"")]
    [InlineData("has_ender_dragon_fight", "null")]
    [InlineData("has_ender_dragon_fight", "\"true\"")]
    public void RejectsInvalidFormat121DimensionFields(string field, string value)
    {
        using var document = Format121Data.Read("dimension_type");
        var root = JsonNode.Parse(document.RootElement.GetProperty("minecraft:overworld").GetRawText())!;
        root[field] = JsonNode.Parse(value);

        var error = Assert.Throws<InvalidDataException>(() => PluginCodecs.Parse(
            PluginCodecs.DimensionType,
            "codecfiles:invalid",
            "format 121",
            Encoding.UTF8.GetBytes(root.ToJsonString())));

        Assert.Contains(field, error.Message);
    }
}

internal static class Format121Data
{
    public static JsonDocument Read(string asset)
    {
        var resource = $"Obsidian.Assets.data_pack.{asset}.json";
        using var stream = typeof(Server).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing vanilla data: {resource}");

        return JsonDocument.Parse(stream);
    }

    public static JsonNode? ToJson(INbtTag tag)
    {
        switch (tag)
        {
            case NbtCompound compound:
                var obj = new JsonObject();

                foreach (var (name, child) in compound)
                {
                    obj[name] = ToJson(child);
                }

                return obj;
            case NbtList list:
                var array = new JsonArray();

                foreach (var child in list)
                {
                    array.Add(ToJson(child));
                }

                return array;
            case NbtTag<string> text when tag.Name == "error_message":
                return JsonNode.Parse(text.Value);
            case NbtTag<string> text:
                return JsonValue.Create(text.Value);
            case NbtTag<byte> flag:
                return JsonValue.Create(flag.Value != 0);
            case NbtTag<int> number:
                return JsonValue.Create(number.Value);
            case NbtTag<float> number:
                return JsonValue.Create(number.Value);
            case NbtTag<double> number:
                return JsonValue.Create(number.Value);
            default:
                throw new InvalidOperationException($"Unexpected codec tag: {tag.GetType().Name}");
        }
    }
}

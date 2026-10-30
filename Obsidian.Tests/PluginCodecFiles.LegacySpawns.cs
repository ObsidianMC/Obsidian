using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Registries.Codecs;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

public partial class PluginCodecFiles
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void MigratesLegacyBiomeSpawns(bool hasSpawners, bool hasCosts, bool hasProbability)
    {
        var patch = new JsonObject
        {
            ["attributes"] = new JsonObject
            {
                ["minecraft:gameplay/can_start_raid"] = false
            }
        };

        if (hasSpawners)
        {
            patch["spawners"] = JsonNode.Parse("""
                { "monster": [{ "type": "minecraft:zombie", "weight": 100, "minCount": 1, "maxCount": 5 }] }
                """);
        }

        if (hasCosts)
        {
            patch["spawn_costs"] = JsonNode.Parse("""
                { "minecraft:zombie": { "energy_budget": 0.12, "charge": 0.7 } }
                """);
        }

        if (hasProbability)
        {
            patch["creature_spawn_probability"] = 0.25f;
        }

        using var document = JsonDocument.Parse(Patched(MinimalBiome, patch.ToJsonString()));

        var biome = BiomeData.Read(document.RootElement);
        var element = biome.ToElement();

        Assert.False(biome.Attributes.Apply("minecraft:gameplay/can_start_raid", true));
        Assert.Equal(hasProbability ? 0.25f : 0.1f, element.CreatureSpawnProbability);

        if (hasProbability)
        {
            var probability = biome.Attributes.Find("minecraft:gameplay/creature_world_gen_spawn_probability");
            Assert.Equal(0.25f, Assert.IsType<NbtTag<float>>(probability).Value);
        }

        if (hasSpawners || hasCosts)
        {
            var attribute = biome.Attributes.Find("minecraft:gameplay/natural_mob_spawns");
            var overlay = Assert.IsType<NbtCompound>(attribute);
            Assert.Equal("overlay", overlay.GetString("modifier"));

            var spawns = Assert.IsType<NbtCompound>(overlay["argument"]);
            Assert.IsType<NbtCompound>(spawns["spawns_by_category"]);
            Assert.IsType<NbtCompound>(spawns["spawn_costs"]);
        }

        if (hasSpawners)
        {
            var mob = Assert.Single(element.Spawners["monster"]);
            Assert.Equal("minecraft:zombie", mob.Type);
            Assert.Equal(100, mob.Weight);
            Assert.Equal(1, mob.MinCount);
            Assert.Equal(5, mob.MaxCount);
            Assert.NotNull(mob.Count);

            var count = Assert.IsType<NbtCompound>(IntProviderCodec.ToNbt(mob.Count, "count"));
            Assert.Equal("minecraft:uniform", count.GetString("type"));
            Assert.Equal(1, Assert.IsType<NbtTag<int>>(count["min_inclusive"]).Value);
            Assert.Equal(5, Assert.IsType<NbtTag<int>>(count["max_inclusive"]).Value);
        }
        else
        {
            Assert.Empty(element.Spawners);
        }

        if (hasCosts)
        {
            var cost = Assert.Single(element.SpawnCosts);
            Assert.Equal("minecraft:zombie", cost.Key);
            Assert.Equal(0.12, cost.Value.EnergyBudget);
            Assert.Equal(0.7, cost.Value.Charge);
        }
        else
        {
            Assert.Empty(element.SpawnCosts);
        }
    }

    // Plugin data is read leniently, like vanilla's Gson, so migrating legacy spawns mustn't reject what loaded before.
    [Fact]
    public void MigratesLegacyBiomeSpawnsWrittenWithCommentsAndTrailingCommas()
    {
        var patch = """
            { "spawners": { "monster": [{ "type": "minecraft:zombie", "weight": 100, "minCount": 1, "maxCount": 5 }] } }
            """;
        var strict = Encoding.UTF8.GetString(Patched(MinimalBiome, patch)).Trim();

        var lenient = "{ // written by hand\n" + strict[1..^1].TrimEnd() + ",\n}";

        var exception = Record.Exception(() => PluginCodecs.Parse(
            PluginCodecs.Biome,
            "minecraft:plains",
            "lenient legacy biome override",
            Encoding.UTF8.GetBytes(lenient)));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("""
        { "spawners": {}, "attributes": {
          "minecraft:gameplay/natural_mob_spawns": { "spawns_by_category": {}, "spawn_costs": {} }
        } }
        """)]
    [InlineData("""
        { "spawn_costs": {}, "attributes": {
          "gameplay/natural_mob_spawns": { "spawns_by_category": {}, "spawn_costs": {} }
        } }
        """)]
    [InlineData("""
        { "creature_spawn_probability": 0.25, "attributes": {
          "minecraft:gameplay/creature_world_gen_spawn_probability": 0.5
        } }
        """)]
    [InlineData("""
        { "creature_spawn_probability": 0.25, "attributes": {
          "gameplay/creature_world_gen_spawn_probability": { "modifier": "multiply", "argument": 2 }
        } }
        """)]
    [InlineData("""{ "spawners": { "invalid_category": [] } }""")]
    [InlineData("""
        { "spawners": { "monster": [
          { "type": "minecraft:zombie", "weight": 100, "minCount": 5, "maxCount": 1 }
        ] } }
        """)]
    [InlineData("""
        { "spawners": { "monster": [{ "type": "minecraft:zombie", "weight": 100, "maxCount": 5 }] } }
        """)]
    [InlineData("""{ "spawn_costs": { "minecraft:zombie": { "charge": 0.7 } } }""")]
    [InlineData("""{ "creature_spawn_probability": 1 }""")]
    public void RejectsAmbiguousOrInvalidLegacyBiomeSpawns(string patch)
    {
        var bytes = Patched(MinimalBiome, patch);

        Assert.Throws<InvalidDataException>(() => PluginCodecs.Parse(
            PluginCodecs.Biome,
            "minecraft:plains",
            "legacy biome override",
            bytes));
    }
}

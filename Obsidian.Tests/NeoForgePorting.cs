using Obsidian.API.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Porting;
using Obsidian.Registries.Codecs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The NeoForge porting tool (Obsidian.Porting): biome modifiers, mod metadata and a whole mod jar.
/// </summary>
public sealed class NeoForgePorting
{
    private const string ModifierPath = "data/test/neoforge/biome_modifier/modifier.json";

    private const string OutputPath = "data/test/biome_modifier/modifier.json";

    // Name, NeoForge's modifier, and Obsidian's modifier it becomes, or what the report says when it can't, or what
    // its note says when it converts with one.
    public static TheoryData<string, string, string, string> Modifiers => new()
    {
        {
            "add features",
            """
            {"type": "neoforge:add_features", "biomes": "#minecraft:is_overworld",
             "features": "minecraft:ore_diamond", "step": "underground_ores"}
            """,
            """
            {"biomes": "#minecraft:is_overworld",
             "add": {"features": {"underground_ores": "minecraft:ore_diamond"}}}
            """,
            null
        },
        {
            "remove features from every step",
            """
            {"type": "neoforge:remove_features", "biomes": ["minecraft:plains", "minecraft:forest"],
             "features": ["minecraft:ore_coal_upper"]}
            """,
            """
            {"biomes": ["minecraft:plains", "minecraft:forest"],
             "remove": {"features": "minecraft:ore_coal_upper"}}
            """,
            null
        },
        {
            "remove features from some steps",
            """
            {"type": "neoforge:remove_features", "biomes": "minecraft:plains", "features": "#test:ores",
             "steps": ["underground_ores", "raw_generation"]}
            """,
            """
            {"biomes": "minecraft:plains",
             "remove": {"features": {"raw_generation": "#test:ores", "underground_ores": "#test:ores"}}}
            """,
            null
        },
        {
            "add spawns to a set less a complement",
            """
            {"type": "neoforge:add_spawns",
             "biomes": {"type": "neoforge:and", "values": [
               "#minecraft:is_overworld", {"type": "neoforge:not", "value": "#c:is_mushroom"}]},
             "spawners": [{"type": "minecraft:zombie", "weight": 5, "minCount": 1, "maxCount": 2},
                          {"data": {"type": "minecraft:husk", "minCount": 1, "maxCount": 1}, "weight": 3}]}
            """,
            """
            {"biomes": "#minecraft:is_overworld", "exclude": "#c:is_mushroom",
             "add": {"spawners": [{"type": "minecraft:zombie", "weight": 5, "minCount": 1, "maxCount": 2},
                                  {"type": "minecraft:husk", "minCount": 1, "maxCount": 1, "weight": 3}]}}
            """,
            null
        },
        {
            "remove spawns from a union",
            """
            {"type": "neoforge:remove_spawns",
             "biomes": {"type": "neoforge:or", "values": ["#minecraft:is_ocean", "minecraft:beach"]},
             "entity_types": "minecraft:drowned"}
            """,
            """{"biomes": ["#minecraft:is_ocean", "minecraft:beach"], "remove": {"spawners": "minecraft:drowned"}}""",
            null
        },
        {
            "add carvers, ignoring the old carving step",
            """
            {"type": "neoforge:add_carvers", "biomes": "minecraft:desert", "carvers": "minecraft:canyon",
             "step": "air"}
            """,
            """{"biomes": "minecraft:desert", "add": {"carvers": "minecraft:canyon"}}""",
            null
        },
        {
            "remove carvers",
            """{"type": "neoforge:remove_carvers", "biomes": "minecraft:desert", "carvers": "minecraft:cave"}""",
            """{"biomes": "minecraft:desert", "remove": {"carvers": "minecraft:cave"}}""",
            null
        },
        {
            "add spawn costs",
            """
            {"type": "neoforge:add_spawn_costs", "biomes": "minecraft:plains",
             "entity_types": ["minecraft:zombie", "minecraft:husk"],
             "spawn_cost": {"energy_budget": 0.12, "charge": 1.0}}
            """,
            """
            {"biomes": "minecraft:plains", "add": {"spawn_costs": {
              "minecraft:zombie": {"energy_budget": 0.12, "charge": 1.0},
              "minecraft:husk": {"energy_budget": 0.12, "charge": 1.0}}}}
            """,
            "in every biome"
        },
        {
            "remove spawn costs",
            """
            {"type": "neoforge:remove_spawn_costs", "biomes": "minecraft:soul_sand_valley",
             "entity_types": "minecraft:skeleton"}
            """,
            """{"biomes": "minecraft:soul_sand_valley", "remove": {"spawn_costs": "minecraft:skeleton"}}""",
            "in every biome"
        },
        {
            "a union's entries once",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:drowned",
             "biomes": {"type": "neoforge:or", "values": ["#minecraft:is_ocean",
               {"type": "neoforge:or", "values": ["minecraft:beach", "#minecraft:is_ocean"]}]}}
            """,
            """{"biomes": ["#minecraft:is_ocean", "minecraft:beach"], "remove": {"spawners": "minecraft:drowned"}}""",
            null
        },
        {
            "a union of complements, less what it lists",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:zombie",
             "biomes": {"type": "neoforge:and", "values": ["#minecraft:is_overworld", {"type": "neoforge:or",
               "values": [{"type": "neoforge:not", "value": ["minecraft:desert", "minecraft:badlands"]},
                          {"type": "neoforge:not", "value": ["minecraft:desert", "minecraft:savanna"]},
                          "minecraft:savanna"]}]}}
            """,
            """
            {"biomes": "#minecraft:is_overworld", "exclude": "minecraft:desert",
             "remove": {"spawners": "minecraft:zombie"}}
            """,
            null
        },
        {
            "identifiers with and without the minecraft namespace are one entry",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:zombie",
             "biomes": {"type": "neoforge:and", "values": ["#minecraft:is_overworld", {"type": "neoforge:or",
               "values": [{"type": "neoforge:not", "value": "desert"},
                          {"type": "neoforge:not", "value": "minecraft:desert"}]}]}}
            """,
            """
            {"biomes": "#minecraft:is_overworld", "exclude": "desert",
             "remove": {"spawners": "minecraft:zombie"}}
            """,
            null
        },
        {
            "a union of complements of different tags",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:zombie",
             "biomes": {"type": "neoforge:and", "values": ["#minecraft:is_overworld", {"type": "neoforge:or",
               "values": [{"type": "neoforge:not", "value": "#c:is_hot"},
                          {"type": "neoforge:not", "value": "#c:is_mushroom"}]}]}}
            """,
            null,
            "excluding different tags"
        },
        {
            "a complement with an entry its tag may hold",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:zombie",
             "biomes": {"type": "neoforge:and", "values": ["#minecraft:is_overworld", {"type": "neoforge:or",
               "values": [{"type": "neoforge:not", "value": "#c:is_hot"}, "minecraft:desert"]}]}}
            """,
            null,
            "may share"
        },
        {
            "features of tags that may overlap",
            """
            {"type": "neoforge:add_features", "biomes": "minecraft:plains", "step": "underground_ores",
             "features": {"type": "neoforge:or", "values": ["#test:ores", "minecraft:ore_diamond"]}}
            """,
            null,
            "may share entries"
        },
        {
            "every biome",
            """
            {"type": "neoforge:add_features", "biomes": {"type": "neoforge:any"},
             "features": "minecraft:ore_diamond", "step": "underground_ores"}
            """,
            null,
            "every biome"
        },
        {
            "an intersection",
            """
            {"type": "neoforge:remove_spawns", "entity_types": "minecraft:zombie",
             "biomes": {"type": "neoforge:and", "values": ["#minecraft:is_overworld", "#c:is_hot"]}}
            """,
            null,
            "intersection"
        },
        {
            "spawn costs of a tag",
            """
            {"type": "neoforge:add_spawn_costs", "biomes": "minecraft:plains", "entity_types": "#test:mobs",
             "spawn_cost": {"energy_budget": 0.1, "charge": 0.5}}
            """,
            null,
            "tag of entity types"
        },
        {
            "load conditions",
            """
            {"type": "neoforge:remove_carvers", "biomes": "minecraft:desert", "carvers": "minecraft:cave",
             "neoforge:conditions": [{"type": "neoforge:mod_loaded", "modid": "other"}]}
            """,
            null,
            "load conditions"
        },
        { "nothing", """{"type": "neoforge:none"}""", null, "does nothing" },
        {
            "another loader's type",
            """{"type": "other:modifier", "biomes": "minecraft:plains"}""",
            null,
            "other:modifier"
        }
    };

    [Theory]
    [MemberData(nameof(Modifiers))]
    public void BiomeModifier_ConvertsToObsidiansFormat(string name, string neoForge, string expected, string reason)
    {
        var (output, report) = ModPorter.Port(new Dictionary<string, byte[]>
        {
            [ModifierPath] = Encoding.UTF8.GetBytes(neoForge)
        });

        if (expected is null)
        {
            Assert.False(output.ContainsKey(OutputPath), name);
            Assert.Contains(
                report.NotConverted,
                entry => entry.Path == ModifierPath && entry.Reason.Contains(reason));
            return;
        }

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(output[OutputPath])), name);

        if (reason is not null)
            Assert.Contains(report.Notes, note => note.StartsWith(ModifierPath) && note.Contains(reason));

        // Obsidian reads what the tool writes.
        BiomeModifierData.Read(JsonDocument.Parse(output[OutputPath]).RootElement);
    }

    [Fact]
    public void ForgeEraMod_UsesTheSharedModifierAndMetadataConverters()
    {
        foreach (var row in Modifiers)
        {
            var json = (string)row[1];
            if (json.Contains("spawn_costs") || json.Contains("carvers"))
                continue;

            var path = "data/test/forge/biome_modifier/nested/modifier.json";
            var (output, report) = ModPorter.Port(new Dictionary<string, byte[]>
            {
                [path] = Encoding.UTF8.GetBytes(json.Replace("neoforge:", "forge:"))
            });
            var destination = "data/test/biome_modifier/nested/modifier.json";

            if (row[2] is string expected)
            {
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(output[destination])));
                BiomeModifierData.Read(JsonDocument.Parse(output[destination]).RootElement);
            }
            else
            {
                Assert.False(output.ContainsKey(destination));
                Assert.Contains(report.NotConverted, entry => entry.Path == path);
            }

            Assert.False(output.ContainsKey(path));
        }

        var (metadata, _) = ModPorter.Port(new Dictionary<string, byte[]>
        {
            ["META-INF/mods.toml"] = Encoding.UTF8.GetBytes("""
                [[mods]]
                modId = "legacy"
                version = "1.0.0"
                [[dependencies.legacy]]
                modId = "forge"
                mandatory = true
                [[dependencies.legacy]]
                modId = "library"
                mandatory = false
                """)
        });
        var info = PluginMetadata.Parse(metadata["obsidian.mod.json"]).ToPluginInfo();
        Assert.Equal("legacy", info.Id);
        var dependency = Assert.Single(info.Dependencies);
        Assert.Equal("library", dependency.Id);
        Assert.Equal(PluginDependencyType.Optional, dependency.Type);
    }

    [Fact]
    public void ModJar_PortsDataAndMetadataAndReportsTheRest()
    {
        const string toml = """
            modLoader = "javafml" # NeoForge's Java loader
            loaderVersion = "[4,)"
            license = 'MIT'

            [[mods]]
            modId = "ruby_gems"
            version = "${file.jarVersion}"
            displayName = "Ruby Gems"
            authors = "Tides, Someone Else"
            displayURL = "https://example.com/ruby"
            logoFile = "logo.png"
            description = '''
            Adds rubies.
            '''

            [[dependencies.ruby_gems]]
                modId = "neoforge"
                type = "required"
                versionRange = "[21.11,)"
            [[dependencies.ruby_gems]]
                modId = "gemlib"
                type = "optional"
                versionRange = "[1.0,2.0)"
                ordering = "AFTER"
                side = "BOTH"
            """;

        // Two overworld modifiers in a row merge; the nether one between the last two keeps them apart.
        static byte[] Ores(string biomes, string feature) => Encoding.UTF8.GetBytes(
            $$"""
            {"type": "neoforge:add_features", "biomes": "{{biomes}}", "features": "{{feature}}",
             "step": "underground_ores"}
            """);

        const string modifiers = "data/ruby_gems/neoforge/biome_modifier";

        var files = new Dictionary<string, byte[]>
        {
            ["META-INF/neoforge.mods.toml"] = Encoding.UTF8.GetBytes(toml),
            ["META-INF/MANIFEST.MF"] = Encoding.UTF8.GetBytes("Implementation-Version: 2.1.0\r\n"),
            ["com/example/ruby/RubyMod.class"] = [0xCA, 0xFE],
            ["pack.mcmeta"] = Encoding.UTF8.GetBytes("""{"pack": {"pack_format": 75}}"""),
            ["assets/ruby_gems/lang/en_us.json"] = Encoding.UTF8.GetBytes("""{"item.ruby_gems.ruby": "Ruby"}"""),
            ["data/ruby_gems/tags/block/ores.json"] = Encoding.UTF8.GetBytes("""{"values": [], "remove": []}"""),
            ["data/ruby_gems/recipe/ruby.json"] = Encoding.UTF8.GetBytes(
                """{"neoforge:conditions": [{"type": "neoforge:mod_loaded", "modid": "gemlib"}]}"""),
            ["data/ruby_gems/neoforge/structure_modifier/villages.json"] = Encoding.UTF8.GetBytes("{}"),
            ["data/ruby_gems/loot_modifiers/extra.json"] = Encoding.UTF8.GetBytes("{}"),
            ["data/neoforge/data_maps/item/compostables.json"] = Encoding.UTF8.GetBytes("{}"),
            [$"{modifiers}/a_rubies.json"] = Ores("#minecraft:is_overworld", "minecraft:ore_iron"),
            [$"{modifiers}/b_sapphires.json"] = Ores("#minecraft:is_overworld", "minecraft:ore_gold"),
            [$"{modifiers}/c_nether.json"] = Ores("#minecraft:is_nether", "minecraft:ore_quartz"),
            [$"{modifiers}/d_emeralds.json"] = Ores("#minecraft:is_overworld", "minecraft:ore_emerald")
        };

        // Read from a jar, as the tool reads one.
        var jar = Path.Join(Path.GetTempPath(), $"obsidian-porting-{Guid.NewGuid():N}.jar");
        try
        {
            using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
            {
                foreach (var (path, data) in files)
                {
                    using var entry = archive.CreateEntry(path).Open();
                    entry.Write(data);
                }
            }

            var (output, report) = ModPorter.Port(ModPorter.ReadMod(jar));

            // The metadata, as Obsidian reads it: the jar's version, and the dependencies but the loader.
            var info = PluginMetadata.Parse(output["obsidian.mod.json"]).ToPluginInfo();
            Assert.Equal("ruby_gems", info.Id);
            Assert.Equal("Ruby Gems", info.Name);
            Assert.Equal(new Version(2, 1, 0, 0), info.Version);
            Assert.Equal("RubyGems", info.AssemblyName);
            Assert.Equal(["Tides", "Someone Else"], info.Authors.AsEnumerable());
            Assert.Equal("Adds rubies.", info.Description);
            Assert.Equal(new Uri("https://example.com/ruby"), info.ProjectUrl);

            var dependency = Assert.Single(info.Dependencies);
            Assert.Equal("gemlib", dependency.Id);
            Assert.Equal(PluginDependencyType.Optional, dependency.Type);
            Assert.Equal("[1.0,2.0)", dependency.Version);
            Assert.Equal(PluginDependencyOrdering.After, dependency.Ordering);

            // Data and assets in vanilla's formats are copied; NeoForge's own are reported.
            Assert.Equal(files["data/ruby_gems/tags/block/ores.json"], output["data/ruby_gems/tags/block/ores.json"]);
            Assert.True(output.ContainsKey("assets/ruby_gems/lang/en_us.json"));
            string[] reported =
            [
                "META-INF/neoforge.mods.toml",
                "data/neoforge/data_maps/item/compostables.json",
                "data/ruby_gems/loot_modifiers/extra.json",
                "data/ruby_gems/neoforge/structure_modifier/villages.json",
                "data/ruby_gems/recipe/ruby.json",
                "pack.mcmeta"
            ];
            Assert.Equal(reported, report.NotConverted.Select(entry => entry.Path).Order(StringComparer.Ordinal));
            Assert.Contains(report.Notes, note => note.Contains("Java code can't be ported (1 .class files)"));

            // Consecutive modifiers with the same biomes merge, in order; the others stay apart.
            string[] converted =
            [
                "data/ruby_gems/biome_modifier/a_rubies.json",
                "data/ruby_gems/biome_modifier/c_nether.json",
                "data/ruby_gems/biome_modifier/d_emeralds.json"
            ];
            Assert.Equal(converted, output.Keys.Where(path => path.Contains("/biome_modifier/")));

            var merged = JsonNode.Parse(output["data/ruby_gems/biome_modifier/a_rubies.json"])!;
            Assert.Equal(
                """["minecraft:ore_iron","minecraft:ore_gold"]""",
                merged["add"]!["features"]!["underground_ores"]!.ToJsonString());

            Assert.Equal(report.ToString(), Encoding.UTF8.GetString(output[ModPorter.ReportFile]));
        }
        finally
        {
            File.Delete(jar);
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData.Structures;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Tests that override vanilla's dimension types for a while, which run alone: other tests generate vanilla's
/// dimensions with them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VanillaDimensionTypesCollection
{
    public const string Name = "Vanilla dimension types";
}

/// <summary>
/// The end of startup, which reads the data packs folder after the plugins and settles vanilla's dimension types.
/// </summary>
[Collection(VanillaDimensionTypesCollection.Name)]
public sealed class DataPackStartup : IDisposable
{
    private const string PackMetadata = """{ "pack": { "description": "", "min_format": 121, "max_format": 121 } }""";

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-startup-packs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(this.folder))
            Directory.Delete(this.folder, recursive: true);
    }

    [Fact]
    public async Task Format121PacksOverridePluginsAndSurviveTheirUnload()
    {
        bool[] cases =
        [
            false,
            true
        ];

        await FixtureCases.AssertAllAsync(
            cases,
            row => $"zipped={row}",
            AssertFormat121PacksOverridePluginsAndSurviveTheirUnload);
    }

    private async Task AssertFormat121PacksOverridePluginsAndSurviveTheirUnload(bool zipped)
    {
        using var biomes = Format121Data.Read("biome");
        using var metadata = Format121Data.Read("pack");
        var biome = JsonNode.Parse(biomes.RootElement.GetProperty("minecraft:plains").GetRawText())!.AsObject();

        // Generation is tested separately; this pack exercises the current biome and spawn codecs at startup.
        biome["features"] = new JsonArray();
        biome["carvers"] = new JsonArray();
        biome["temperature"] = 0.25f;
        var rootBiome = biome.ToJsonString();
        biome["temperature"] = 0.75f;
        var overlayBiome = biome.ToJsonString();

        var packMetadata = JsonNode.Parse(metadata.RootElement.GetRawText())!.AsObject();
        packMetadata["overlays"] = JsonNode.Parse("""
            { "entries": [
              { "directory": "current", "min_format": [121, 0], "max_format": [121, 0] },
              { "directory": "future", "min_format": [121, 1], "max_format": [121, 1] }
            ] }
            """);

        const string biomePath = "data/minecraft/worldgen/biome/plains.json";
        var files = new Dictionary<string, string>
        {
            ["pack.mcmeta"] = packMetadata.ToJsonString(),
            [biomePath] = rootBiome,
            [$"current/{biomePath}"] = overlayBiome,
            [$"future/{biomePath}"] = "{}"
        };
        // Each case gets its own packs and plugin, so the other case's pack can't stand in for this one's.
        var root = Path.Join(this.folder, zipped ? "zipped" : "directory");
        var packs = Path.Join(root, "packs");
        Directory.CreateDirectory(packs);

        if (zipped)
        {
            using var zip = ZipFile.Open(Path.Join(packs, "format121.zip"), ZipArchiveMode.Create);

            foreach (var (path, json) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(json);
            }
        }
        else
        {
            foreach (var (path, json) in files)
            {
                var file = Path.Join(packs, "format121", path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, json);
            }
        }

        biome["temperature"] = 0.5f;
        var pluginFiles = new Dictionary<string, byte[]>
        {
            [biomePath] = Encoding.UTF8.GetBytes(biome.ToJsonString())
        };
        var pluginPath = Path.Join(root, "probe.obby");
        File.WriteAllBytes(pluginPath, PluginLoading.ProbeArchive("1.0.0", pluginFiles));
        var vanilla = CodecRegistry.Biomes.Plains.Element;
        var manager = CreateManager();

        try
        {
            var plugin = await manager.LoadPluginAsync(pluginPath);
            Assert.NotNull(plugin);
            Assert.Equal(0.5f, CodecRegistry.Biomes.Plains.Element.Temperature);

            manager.FinishStartup(packs);

            var applied = CodecRegistry.Biomes.Plains.Element;
            Assert.Equal(0.75f, applied.Temperature);
            Assert.NotEmpty(applied.Spawners);
            Assert.NotNull(applied.Spawners["creature"][0].Count);

            await manager.UnloadPluginAsync(plugin);

            Assert.Same(applied, CodecRegistry.Biomes.Plains.Element);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginSpawns.Apply([], [], NullLogger.Instance);
            Directory.Delete(root, recursive: true);
        }

        Assert.Same(vanilla, CodecRegistry.Biomes.Plains.Element);
    }

    [Fact]
    public void Format121OverlayRangesSelectCurrentMinor()
    {
        (string Min, string Max, bool Applies)[] cases =
        [
            ("121", "121", true),
            ("[121, 0]", "[121, 0]", true),
            ("[120, 9]", "[120, 9]", false),
            ("[121, 1]", "[121, 1]", false)
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"min={row.Min}, max={row.Max}, applies={row.Applies}",
            row => AssertFormat121OverlayRangesSelectCurrentMinor(row.Min, row.Max, row.Applies));
    }

    private static void AssertFormat121OverlayRangesSelectCurrentMinor(string min, string max, bool applies)
    {
        using var vanilla = Format121Data.Read("pack");
        var metadata = JsonNode.Parse(vanilla.RootElement.GetRawText())!.AsObject();
        metadata["overlays"] = JsonNode.Parse($$"""
            { "entries": [{ "directory": "selected", "min_format": {{min}}, "max_format": {{max}} }] }
            """);
        var logger = new PluginFeatureFiles.ListLogger();

        var read = DataPackMetadata.Read("format121", Encoding.UTF8.GetBytes(metadata.ToJsonString()), logger);

        Assert.Equal(applies, read.Overlays.Contains("selected"));
        Assert.Empty(logger.Messages);
    }

    [Fact(DisplayName = "A stem startup can't read until a dimension type override is left out is read once it is")]
    public async Task ReadsStemsAgainOnceDimensionTypesSettle()
    {
        // The overworld's type from y 0, which vanilla's overworld noise settings, from -64, don't fit: the stem using
        // them can't be read until startup leaves the type override out.
        const string lowOverworld = """
            {
              "has_skylight": true,
              "has_ceiling": false,
              "has_ender_dragon_fight": false,
              "coordinate_scale": 1,
              "min_y": 0,
              "height": 256,
              "logical_height": 256,
              "infiniburn": "#minecraft:infiniburn_overworld",
              "ambient_light": 0,
              "monster_spawn_light_level": 7,
              "monster_spawn_block_light_limit": 0
            }
            """;

        const string plains = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;

        var pack = Path.Join(this.folder, "plains");
        var data = Path.Join(pack, "data");
        Directory.CreateDirectory(Path.Join(data, "minecraft", "dimension_type"));
        Directory.CreateDirectory(Path.Join(data, "startuptest", "dimension"));
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), PackMetadata);
        File.WriteAllText(Path.Join(data, "minecraft", "dimension_type", "overworld.json"), lowOverworld);
        File.WriteAllText(Path.Join(data, "startuptest", "dimension", "plains.json"), plains);

        var manager = CreateManager();

        try
        {
            manager.FinishStartup(this.folder);

            Assert.Equal(-64, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);

            var dimension = PluginDimensions.Find("startuptest:plains").Noise();
            Assert.Equal("minecraft:plains", Assert.Single(dimension.PossibleBiomes).Name);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginDimensions.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    [Fact(DisplayName = "A vanilla stem startup kept settles its type with the noise settings a later pack adds")]
    public async Task SettlesTypesWithTheNoiseSettingsOfKeptStems()
    {
        // A plugin's overworld stem names noise settings from y 0 to 511 that only a data pack read after it adds,
        // with the overworld's type over the same range: the type fits the stem, so it stays.
        const string tallOverworld = """
            {
              "has_skylight": true,
              "has_ceiling": false,
              "has_ender_dragon_fight": false,
              "coordinate_scale": 1,
              "min_y": 0,
              "height": 512,
              "logical_height": 512,
              "infiniburn": "#minecraft:infiniburn_overworld",
              "ambient_light": 0,
              "monster_spawn_light_level": 7,
              "monster_spawn_block_light_limit": 0
            }
            """;

        const string tallSettings = """
            {
              "sea_level": 63,
              "disable_mob_generation": false,
              "legacy_random_source": false,
              "default_block": "minecraft:stone",
              "default_fluid": { "Name": "minecraft:water", "Properties": { "level": "0" } },
              "noise": { "min_y": 0, "height": 512 },
              "noise_router": {
                "temperature": 0,
                "vegetation": 0,
                "continents": 0,
                "erosion": 0,
                "depth": 0,
                "ridges": 0,
                "chunk_surface_level": 0,
                "final_density": 0
              },
              "spawn_target": [],
              "material_rule": { "type": "minecraft:sequence", "sequence": [] }
            }
            """;

        const string overworld = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "startuptest:tall",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;

        var pack = Path.Join(this.folder, "packs", "tall");
        var data = Path.Join(pack, "data");
        Directory.CreateDirectory(Path.Join(data, "minecraft", "dimension_type"));
        Directory.CreateDirectory(Path.Join(data, "startuptest", "worldgen", "noise_settings"));
        File.WriteAllText(Path.Join(pack, "pack.mcmeta"), PackMetadata);
        File.WriteAllText(Path.Join(data, "minecraft", "dimension_type", "overworld.json"), tallOverworld);
        File.WriteAllText(Path.Join(data, "startuptest", "worldgen", "noise_settings", "tall.json"), tallSettings);

        var plugin = Path.Join(this.folder, "probe.obby");
        var stem = new Dictionary<string, byte[]>
        {
            ["data/minecraft/dimension/overworld.json"] = Encoding.UTF8.GetBytes(overworld)
        };
        await File.WriteAllBytesAsync(plugin, PluginLoading.ProbeArchive("1.0.0", stem));

        var manager = CreateManager();

        try
        {
            Assert.NotNull(await manager.LoadPluginAsync(plugin));
            manager.FinishStartup(Path.Join(this.folder, "packs"));

            Assert.Equal(0, CodecRegistry.Dimensions.All["minecraft:overworld"].Element.MinY);
            Assert.Equal("startuptest:tall", PluginDimensions.Find("minecraft:overworld").Noise().SettingsId);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginNoise.Apply([], [], NullLogger.Instance);
            PluginDimensions.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
        }
    }

    [Fact(DisplayName = "A pack written for older versions loads whole, like Terralith's does in vanilla")]
    public async Task ReadsPacksWrittenForOlderVersions()
    {
        // Each file holds what one of Terralith's left out: types without a namespace, a named block-state provider,
        // a fluid tag, a template pool's old "name" field, a 1.20.1 template's renamed blocks, and the loot functions
        // and location check its chests use. Material rules were left out silently, so they're checked by reading.
        var files = new Dictionary<string, byte[]>
        {
            ["pack.mcmeta"] = Encoding.UTF8.GetBytes(PackMetadata),
            ["data/startuptest/worldgen/material_rule/surface.json"] = Json(MaterialRule),
            ["data/startuptest/worldgen/block_state_provider/patch.json"] = Json(StateProvider),
            ["data/startuptest/worldgen/feature/patch.json"] = Json(Feature),
            ["data/startuptest/worldgen/placed_feature/patch.json"] = Json(PlacedFeature),
            ["data/startuptest/worldgen/template_pool/hut.json"] = Json(TemplatePool),
            ["data/startuptest/structure/hut.nbt"] = LegacyTemplate(),
            ["data/startuptest/loot_table/chest.json"] = Json(ChestLoot)
        };

        foreach (var (path, bytes) in files)
        {
            var file = Path.Join(this.folder, "older", path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }

        var logger = new WarningLogger();
        var manager = CreateManager(logger);

        try
        {
            manager.FinishStartup(this.folder);

            Assert.Empty(logger.Messages);
            Assert.Equal("startuptest", PluginWorldgen.Data.Read("material_rule", "startuptest:surface")
                .GetProperty("marker").GetString());
            Assert.NotNull(PluginFeatures.CurrentRegistry.FeatureSet!.FindPlaced("startuptest:patch"));

            // The template's old blocks are 26.3's, not just any block that loads.
            var template = PluginStructures.ParseTemplate("startuptest:hut", "older", LegacyTemplate(), 1 << 20)
                .File.Template;
            Assert.Single(template.FilterBlocks(Vector.Zero, new StructurePlaceSettings(), BlockIds.ShortGrass));
            Assert.Single(template.FilterBlocks(Vector.Zero, new StructurePlaceSettings(), BlockIds.IronChain));

            // The instrument comes from a vanilla instrument tag, which has to hold the instruments to pick one.
            var horn = Assert.Single(ChestItems());
            Assert.True(horn.ContainsKey(DataComponentType.Instrument));
            Assert.True(horn.HasEnchantmentGlint);
            Assert.Single(horn.Lore);

            // A float written as a whole number reads like vanilla's, and a patch making a damageable item
            // stackable is undone, as vanilla's applyComponentsAndValidate does.
            Assert.Equal(2f, horn.GetComponent<UseCooldownDataComponent>(DataComponentType.UseCooldown)!.Seconds);
            Assert.Equal(5, horn.MaxDamage);
            Assert.Equal(1, horn.MaxStackSize);

            // Numbers of mixed types in a list read as numbers.
            var model = horn.GetComponent<CustomModelDataComponent>(DataComponentType.CustomModelData)!;
            Assert.Equal<float>([1f, 1.5f], model.Floats);

            // A list of texts mixing strings, objects and lists is its first text with the rest as its extras, at any
            // depth, so a nested list's first text styles the rest.
            Assert.Equal("Wintry ", horn.CustomName!.Text);
            var nested = Assert.Single(horn.CustomName.GetExtras());
            Assert.Equal("Ho", nested.Text);
            Assert.True(nested.Bold);
            Assert.Equal("rn", Assert.Single(nested.GetExtras()).Text);
            Assert.Equal("Horn", Assert.Single(horn.ItemName!.GetExtras()).Text);
        }
        finally
        {
            await manager.DisposeAsync();
            PluginCodecs.Apply([], NullLogger.Instance, levelsCreated: false);
            PluginNoise.Apply([], [], NullLogger.Instance);
            PluginDimensions.Apply([], NullLogger.Instance);
            PluginFeatures.Apply([], [], [], new HashSet<string>(), NullLogger.Instance);
            PluginLootTables.Apply([]);
        }
    }

    private const string MaterialRule = """
        { "type": "block", "result_state": { "id": "minecraft:stone" }, "marker": "startuptest" }
        """;

    private const string StateProvider = """
        { "type": "simple", "state": { "id": "minecraft:moss_block" } }
        """;

    private const string Feature = """
        { "type": "simple_block", "to_place": "startuptest:patch" }
        """;

    private const string PlacedFeature = """
        {
          "feature": "startuptest:patch",
          "placement": [
            { "type": "count", "count": { "type": "constant", "value": 1 } },
            {
              "type": "block_predicate_filter",
              "predicate": {
                "type": "all_of",
                "predicates": [
                  { "type": "not", "predicate": { "type": "matching_fluids", "fluids": "#minecraft:water" } }
                ]
              }
            }
          ]
        }
        """;

    private const string TemplatePool = """
        {
          "name": "hut",
          "fallback": "minecraft:empty",
          "elements": [
            {
              "weight": 1,
              "element": {
                "element_type": "minecraft:single_pool_element",
                "location": "startuptest:hut",
                "processors": "minecraft:empty",
                "projection": "rigid"
              }
            }
          ]
        }
        """;

    private const string ChestLoot = """
        {
          "pools": [
            {
              "rolls": 1,
              "condition": {
                "type": "minecraft:location_check",
                "predicate": { "position": { "y": { "min": 0 } } }
              },
              "entries": [{ "type": "minecraft:item", "name": "minecraft:goat_horn" }],
              "modifier": [
                { "type": "minecraft:set_instrument", "options": "#minecraft:regular_goat_horns" },
                {
                  "type": "minecraft:set_components",
                  "components": {
                    "minecraft:enchantment_glint_override": true,
                    "minecraft:use_cooldown": { "seconds": 2 },
                    "minecraft:max_damage": 5,
                    "minecraft:custom_model_data": { "floats": [1, 1.5] },
                    "minecraft:item_name": ["Goat ", { "text": "Horn" }]
                  }
                },
                { "type": "minecraft:set_components", "components": { "minecraft:max_stack_size": 16 } },
                {
                  "type": "minecraft:set_name",
                  "entity": "this",
                  "name": ["Wintry ", [{ "text": "Ho", "bold": true }, "rn"]]
                },
                {
                  "type": "minecraft:set_lore",
                  "mode": "append",
                  "lore": [
                    {
                      "text": "Found in an igloo",
                      "hover_event": { "action": "show_text", "value": ["Cold ", { "text": "and snowy" }] }
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    // The chest's loot for a chest above y 0, as its location check needs.
    private static List<ItemStack> ChestItems()
    {
        var table = PluginLootTables.Resolve("startuptest:chest");
        Assert.NotNull(table);

        return table.GetRandomItems(new LootContext
        {
            Random = new LegacyRandomSource(1),
            Origin = new VectorD(0.5, 64.5, 0.5)
        });
    }

    // A 1.20.1 template (DataVersion 3465) of a grass block and a chain, which 26.3 calls short grass and an iron
    // chain. Like vanilla's saves, its DataVersion comes after its palette.
    private static byte[] LegacyTemplate()
    {
        using var stream = new MemoryStream();

        using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, ""))
        {
            writer.WriteTag(new NbtList(NbtTagType.Int, "size")
            {
                new NbtTag<int>(string.Empty, 1),
                new NbtTag<int>(string.Empty, 2),
                new NbtTag<int>(string.Empty, 1)
            });
            writer.WriteTag(new NbtList(NbtTagType.Compound, "palette")
            {
                new NbtCompound { new NbtTag<string>("Name", "minecraft:grass") },
                new NbtCompound { new NbtTag<string>("Name", "minecraft:chain") }
            });
            writer.WriteTag(new NbtList(NbtTagType.Compound, "blocks")
            {
                LegacyBlock(y: 0, state: 0),
                LegacyBlock(y: 1, state: 1)
            });
            writer.WriteTag(new NbtList(NbtTagType.Compound, "entities"));
            writer.WriteTag(new NbtTag<int>("DataVersion", 3465));
            writer.EndCompound();
            writer.TryFinish();
        }

        return stream.ToArray();
    }

    private static NbtCompound LegacyBlock(int y, int state) => new()
    {
        new NbtList(NbtTagType.Int, "pos")
        {
            new NbtTag<int>(string.Empty, 0),
            new NbtTag<int>(string.Empty, y),
            new NbtTag<int>(string.Empty, 0)
        },
        new NbtTag<int>("state", state)
    };

    // Records what startup warns of or fails at; reading a pack logs only information when it reads every file.
    private sealed class WarningLogger : ILogger<PluginManager>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (this.IsEnabled(logLevel))
                this.Messages.Add($"{formatter(state, exception)} {exception?.Message}");
        }
    }

    // A plugin manager with no plugins, as the server creates it, during startup: no world is loaded yet.
    private static PluginManager CreateManager(ILogger<PluginManager>? logger = null)
    {
        var world = TestWorldFactory.Create($"fake-server-{Guid.NewGuid():N}");
        var server = new FakeServer(world, worldManager: new FakeWorldManager(world, worldsLoaded: false));

        var services = new ServiceCollection();
        services.AddOptions<ServerConfiguration>();
        services.AddSingleton<IServer>(server);
        var provider = services.BuildServiceProvider();

        var dispatcher = new EventDispatcher(NullLogger<EventDispatcher>.Instance, provider);
        var commands = new CommandHandler(provider, NullLogger<CommandHandler>.Instance);

        return new PluginManager(
            server,
            provider,
            dispatcher,
            commands,
            logger ?? NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
    }
}

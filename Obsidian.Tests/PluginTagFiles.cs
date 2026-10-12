using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Registries;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' tag files, merged with vanilla's tags like NeoForge and Fabric merge mods' data packs.
/// </summary>
/// <remarks>
/// Tags are shared by every test, so the tests that change them run one at a time.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public class PluginTagFiles
{
    [Fact(DisplayName = "Plugins' tag files add to vanilla's tags and make new ones, in load order")]
    public void MergesTagFiles()
    {
        var ore = BlocksRegistry.Register("tagfiles:ore", new BlockProperties());
        var boost = TagsRegistry.Block.SnifferEggHatchBoost;
        var vanillaBoost = boost.Entries;
        var sounds = TagsRegistry.Block.CamelSandStepSoundBlocks;
        var vanillaSounds = sounds.Entries;

        TagFile[] files =
        [
            // Adds to a vanilla tag, which code holding it sees.
            File("block", "minecraft:sniffer_egg_hatch_boost", """{ "values": ["tagfiles:ore"] }"""),

            // A new tag, with an optional entry that doesn't exist.
            File("block", "tagfiles:ores", """
                { "values": ["tagfiles:ore", "stone", { "id": "tagfiles:missing", "required": false }] }
                """),

            // Refers to another tag, and removes an entry like NeoForge's tag files can.
            File("block", "tagfiles:all", """
                { "values": ["#tagfiles:ores", "minecraft:dirt"], "remove": ["minecraft:stone"] }
                """),

            // A required entry that doesn't exist fails the tag, which keeps vanilla's entries.
            File("block", "minecraft:camel_sand_step_sound_blocks", """{ "values": ["tagfiles:missing"] }""")
        ];

        try
        {
            PluginTags.Apply(files, NullLogger.Instance);

            Assert.Equal([.. vanillaBoost, ore.Id], boost.Entries.ToArray());
            Assert.True(TagsRegistry.Contains("block", "minecraft:sniffer_egg_hatch_boost", ore.Id));
            Assert.Equal(vanillaSounds.ToArray(), sounds.Entries.ToArray());

            Assert.True(TagsRegistry.TryGet("block", "tagfiles:ores", out var ores));
            Assert.Equal([ore.Id, 1], ores.Entries.ToArray());
            Assert.True(TagsRegistry.TryGet("block", "tagfiles:all", out var all));
            Assert.Equal([ore.Id, 9], all.Entries.ToArray());
            Assert.Contains(all, TagsRegistry.GetCategories()["block"]);

            // A later plugin's file that replaces the tag drops what came before it.
            PluginTags.Apply(
                [.. files, File("block", "minecraft:sniffer_egg_hatch_boost", """
                    { "replace": true, "values": ["minecraft:dirt"] }
                    """)],
                NullLogger.Instance);

            Assert.Equal([9], boost.Entries.ToArray());
        }
        finally
        {
            // Once no plugin has tag files, the tags are vanilla's again.
            PluginTags.Apply([], NullLogger.Instance);
        }

        Assert.Equal(vanillaBoost.ToArray(), boost.Entries.ToArray());
        Assert.False(TagsRegistry.TryGet("block", "tagfiles:ores", out _));
    }

    [Fact(DisplayName = "Tags resolve from vanilla's definitions, so changes reach the tags that refer to them")]
    public void ResolvesWithVanillaDefinitions()
    {
        var vanilla = TagsRegistry.Block.All
            .Concat(TagsRegistry.Item.All)
            .Concat(TagsRegistry.EntityType.All)
            .Concat(TagsRegistry.Worldgen.Biome.All)
            .Distinct()
            .ToDictionary(tag => tag, tag => tag.Entries.ToArray());

        try
        {
            // Any plugin file makes the tags resolve from vanilla's definitions, which give vanilla's own tags.
            PluginTags.Apply([File("block", "tagfiles:unrelated", """{ "values": [] }""")], NullLogger.Instance);

            Assert.All(vanilla, entry => Assert.Equal(entry.Value, entry.Key.Entries.ToArray()));

            // Dirt added to oak logs is in the logs tags too, which refer to oak logs; a file that replaces a tag
            // drops an earlier failing one.
            PluginTags.Apply(
                [
                    File("block", "minecraft:oak_logs", """{ "values": ["minecraft:dirt"] }"""),
                    File("block", "tagfiles:replaced", """{ "values": ["tagfiles:missing"] }"""),
                    File("block", "tagfiles:replaced", """{ "replace": true, "values": ["minecraft:dirt"] }""")
                ],
                NullLogger.Instance);

            Assert.Contains(9, TagsRegistry.Block.Logs.Entries);
            Assert.True(TagsRegistry.Contains("block", "tagfiles:replaced", 9));

            // Which reference of an optional cycle is left out doesn't depend on the order the files were read.
            TagFile[] cycle =
            [
                File("block", "tagfiles:c", """{ "values": ["stone", { "id": "#tagfiles:d", "required": false }] }"""),
                File("block", "tagfiles:d", """{ "values": ["dirt", { "id": "#tagfiles:c", "required": false }] }""")
            ];

            int[] Entries(string tag) =>
                TagsRegistry.TryGet("block", tag, out var found) ? found.Entries.ToArray() : [];

            PluginTags.Apply(cycle, NullLogger.Instance);
            var c = Entries("tagfiles:c");
            var d = Entries("tagfiles:d");

            PluginTags.Apply(cycle.Reverse(), NullLogger.Instance);
            Assert.Equal(c, Entries("tagfiles:c"));
            Assert.Equal(d, Entries("tagfiles:d"));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }

        Assert.DoesNotContain(9, TagsRegistry.Block.Logs.Entries);

        // A malformed identifier, even in an optional entry, leaves the file out, like vanilla.
        Assert.Throws<System.IO.InvalidDataException>(() => File("block", "tagfiles:x", """
            { "replace": true, "values": ["dirt", { "id": "minecraft:STONE", "required": false }] }
            """));
    }

    [Fact(DisplayName = "Obsidian's convention tags resolve with no plugins, are synced, and plugins add to them")]
    public void ConventionTags()
    {
        using var stream = typeof(PluginTags).Assembly
            .GetManifestResourceStream("Obsidian.Registries.convention_tags.json")!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);

        var shipped = document.RootElement
            .EnumerateObject()
            .Select(tag => tag.Name)
            .ToList();

        var ironOre = TagsRegistry.Block.IronOres.Entries[0];

        try
        {
            PluginTags.Apply([], NullLogger.Instance);

            // A tag missing a required entry would fail and not exist; every shipped tag has entries.
            Assert.All(shipped, name =>
            {
                var registry = PluginTags.Registries.First(registry => name.StartsWith($"{registry}/"));
                var identifier = $"c:{name[(registry.Length + 1)..]}";

                Assert.True(TagsRegistry.TryGet(registry, identifier, out var tag), identifier);
                Assert.NotEmpty(tag.Entries);
            });

            Assert.True(TagsRegistry.Contains("block", "c:ores", ironOre));
            Assert.Contains(TagsRegistry.GetCategories()["worldgen/biome"], tag => tag.Identifier == "c:is_overworld");

            // A plugin's file adds to a convention tag, which reaches the tags that refer to it.
            PluginTags.Apply(
                [File("block", "c:ores/iron", """{ "values": ["minecraft:dirt"] }""")],
                NullLogger.Instance);

            Assert.True(TagsRegistry.Contains("block", "c:ores", 9));
        }
        finally
        {
            PluginTags.Apply([], NullLogger.Instance);
        }

        Assert.False(TagsRegistry.Contains("block", "c:ores", 9));
    }

    private static TagFile File(string registry, string identifier, string json) =>
        PluginTags.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));
}

using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' and data packs' structures, structure sets, template pools and templates, started and placed by world
/// generation.
/// </summary>
/// <remarks>
/// Structure sets are built without becoming the current one, which tests loading plugins would replace; only the
/// test's own templates are made the registry's. These tests run with the other tests applying plugins' files.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public class PluginStructureFiles
{
    private const long Seed = 1234L;

    private const string Tower = "structuretest:tower";

    // A tower of one piece, on the surface of every overworld biome.
    private const string TowerStructure = """
        {
          "type": "minecraft:jigsaw",
          "biomes": "#minecraft:is_overworld",
          "spawn_overrides": {},
          "step": "surface_structures",
          "start_pool": "structuretest:tower/start",
          "size": 0,
          "start_height": { "absolute": 0 },
          "project_start_to_heightmap": "WORLD_SURFACE_WG",
          "max_distance_from_center": 16,
          "use_expansion_hack": false
        }
        """;

    private const string TowerPool = """
        {
          "fallback": "minecraft:empty",
          "elements": [
            {
              "weight": 1,
              "element": {
                "element_type": "minecraft:single_pool_element",
                "location": "structuretest:tower",
                "processors": "minecraft:empty",
                "projection": "rigid"
              }
            }
          ]
        }
        """;

    // A start in every chunk of even coordinates: a spacing of 2 with a separation of 1 leaves one chunk per cell.
    private const string Towers = """
        {
          "structures": [{ "structure": "structuretest:tower", "weight": 1 }],
          "placement": { "type": "minecraft:random_spread", "spacing": 2, "separation": 1, "salt": 1 }
        }
        """;

    // Vanilla's villages, more often and elsewhere.
    private const string DenseVillages = """
        {
          "structures": [
            { "structure": "minecraft:village_plains", "weight": 2 },
            { "structure": "minecraft:village_desert", "weight": 1 },
            { "structure": "minecraft:village_savanna", "weight": 1 },
            { "structure": "minecraft:village_snowy", "weight": 1 },
            { "structure": "minecraft:village_taiga", "weight": 1 }
          ],
          "placement": { "type": "minecraft:random_spread", "spacing": 4, "separation": 2, "salt": 7 }
        }
        """;

    [Fact(DisplayName = "A plugin's jigsaw structure places its template in a chunk, and goes with it")]
    public void PlacesPluginJigsawStructures()
    {
        var structures = Build(TowerFiles());
        Assert.Empty(structures.Owners);
        structures = Only(structures, "structuretest:towers");
        Assert.Equal(Tower, Assert.Single(structures.Templates).Key);

        try
        {
            StructureRegistry.SetOverrides(structures.Templates);

            var builder = new ChunkBuilder(MojangDimension.Overworld, Seed, structures: () => structures);
            var chunks = new FullChunks(builder, MojangDimension.Overworld);

            Assert.Contains(builder.Structures!.Sets, set => set.Identifier == "structuretest:towers");

            // The tower starts at the chunk's corner, turned at random, so part of it may be in the chunks next to it.
            var tower = builder.Structures.GetStarts(0, 0).Single(start => start.Structure.Identifier == Tower);
            var box = Assert.Single(tower.Pieces).BoundingBox;
            var columns = (Math.Min(box.MaxX, 15) - Math.Max(box.MinX, 0) + 1)
                * (Math.Min(box.MaxZ, 15) - Math.Max(box.MinZ, 0) + 1);

            Assert.Equal(3, box.YSpan);
            Assert.Equal(3 * columns, DiamondBlocks(chunks.Get(0, 0)));

            // Once the plugin is gone, chunks not generated yet don't get its structure, even where it started.
            structures = Only(Build([]));

            Assert.DoesNotContain(builder.Structures.GetStarts(8, 8), start => start.Structure.Identifier == Tower);
            Assert.Equal(0, DiamondBlocks(chunks.Get(8, 8)));
        }
        finally
        {
            StructureRegistry.SetOverrides(PluginStructures.Current.Templates);
        }
    }

    [Fact(DisplayName = "A vanilla structure set override moves vanilla's structures, and reads vanilla's sets again")]
    public void OverridesVanillaStructureSets()
    {
        // Vanilla's swamp hut in every overworld biome: vanilla's set of huts names it, so it's read again with it.
        var hut = VanillaOverrides.Entries(PluginStructures.Structure)["minecraft:swamp_hut"].GetRawText()
            .Replace("\"#minecraft:has_structure/swamp_hut\"", "\"#minecraft:is_overworld\"");

        var structures = Build(
        [
            Parse(PluginStructures.StructureSet, "minecraft:villages", DenseVillages),
            Parse(PluginStructures.Structure, "minecraft:swamp_hut", hut)
        ]);

        var huts = structures.Sets["minecraft:swamp_huts"];
        Assert.NotSame(StructureSets.All["minecraft:swamp_huts"], huts);
        Assert.Same(structures.Structures["minecraft:swamp_hut"], Assert.Single(huts.Structures).Structure);
        Assert.NotSame(Structures.All["minecraft:swamp_hut"], structures.Structures["minecraft:swamp_hut"]);

        // Vanilla's plains village at chunk (-9, -52) of seed 12345 isn't in a start chunk of the override's.
        const long villageSeed = 12345L;
        var villages = (RandomSpreadStructurePlacement)structures.Sets["minecraft:villages"].Placement;
        Assert.NotEqual((-9, -52), villages.GetPotentialStructureChunk(villageSeed, -9, -52));

        var onlyVillages = Only(structures, "minecraft:villages");
        var builder = new ChunkBuilder(MojangDimension.Overworld, villageSeed, structures: () => onlyVillages);
        var manager = builder.Structures!;
        Assert.DoesNotContain(manager.GetStarts(-9, -52), IsVillage);

        // Villages start in the override's start chunks around it instead.
        var found = 0;

        for (var cellX = -3; cellX <= -2; cellX++)
        {
            for (var cellZ = -14; cellZ <= -13; cellZ++)
            {
                var (x, z) = villages.GetPotentialStructureChunk(villageSeed, cellX * 4, cellZ * 4);
                found += manager.GetStarts(x, z).Count(IsVillage);
            }
        }

        Assert.True(found > 0);

        static bool IsVillage(StructureStart start) =>
            start.Structure.Identifier.StartsWith("minecraft:village_", StringComparison.Ordinal);
    }

    [Fact(DisplayName = "A start whose structure is gone is kept as saved, and its state comes back with it")]
    public void KeepsStartsOfStructuresThatAreGone()
    {
        var files = TowerFiles();
        var structures = Only(Build(files), "structuretest:towers");
        var manager = new ChunkBuilder(MojangDimension.Overworld, Seed, structures: () => structures).Structures!;

        // The tower is placed in this session, so its state is saved with its start chunk.
        var tower = manager.GetStarts(0, 0).Single(start => start.Structure.Identifier == Tower);
        StructureManager.RestoreStart(tower, null);

        // Its plugin unloads: the start is dropped, but its state is still saved.
        structures = Only(Build([]));
        manager.Refresh();

        Assert.DoesNotContain(manager.GetStarts(0, 0), start => start.Structure.Identifier == Tower);
        var saved = manager.SaveStarts(0, 0, null);
        Assert.True(saved!.TryGetTag<NbtCompound>(Tower, out _));

        // After a restart without the plugin, the chunk loads, and its saved start stays as it was.
        var vanilla = Only(Build([]));
        var restarted = new ChunkBuilder(MojangDimension.Overworld, Seed, structures: () => vanilla).Structures!;
        foreach (var start in restarted.GetStarts(0, 0))
            StructureManager.RestoreStart(start, saved);

        Assert.True(restarted.SaveStarts(0, 0, saved)!.TryGetTag<NbtCompound>(Tower, out _));

        // The plugin loads again: the start made again takes the state the dropped one had.
        structures = Only(Build(files), "structuretest:towers");
        manager.Refresh();

        var again = manager.GetStarts(0, 0).Single(start => start.Structure.Identifier == Tower);
        Assert.NotSame(tower, again);
        Assert.True(again.IsStateRestored);
    }

    private static WorldgenStructures Build(StructureFile[] files) => PluginStructures.Build(
        files,
        files.Length == 0 ? [] : [Template()],
        WorldgenFeatureSet.Vanilla,
        [],
        NullLogger.Instance);

    // Only some of the structure sets, so that starts aren't made for vanilla's others, like strongholds, whose rings
    // take a while to find.
    private static WorldgenStructures Only(WorldgenStructures structures, params string[] sets) => structures with
    {
        Sets = structures.Sets.Where(set => sets.Contains(set.Key)).ToDictionary()
    };

    private static StructureFile[] TowerFiles() =>
    [
        Parse(PluginStructures.Structure, Tower, TowerStructure),
        Parse(PluginStructures.TemplatePool, "structuretest:tower/start", TowerPool),
        Parse(PluginStructures.StructureSet, "structuretest:towers", Towers)
    ];

    private static StructureFile Parse(string registry, string identifier, string json) =>
        PluginStructures.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));

    // A 3 by 3 by 3 cube of diamond blocks, as a structure block saves it.
    private static TemplateFile Template()
    {
        var blocks = new NbtList(NbtTagType.Compound, "blocks");
        for (var i = 0; i < 27; i++)
        {
            blocks.Add(new NbtCompound
            {
                Ints("pos", i % 3, i / 3 % 3, i / 9),
                new NbtTag<int>("state", 0)
            });
        }

        var palette = new NbtList(NbtTagType.Compound, "palette")
        {
            new NbtCompound { new NbtTag<string>("Name", "minecraft:diamond_block") }
        };

        using var stream = new MemoryStream();
        using (var writer = new NbtWriterStream(stream, NbtCompression.GZip, ""))
        {
            writer.WriteTag(Ints("size", 3, 3, 3));
            writer.WriteTag(palette);
            writer.WriteTag(blocks);
            writer.WriteTag(new NbtList(NbtTagType.Compound, "entities"));
            writer.EndCompound();
            writer.TryFinish();
        }

        return PluginStructures.ParseTemplate(Tower, "test", stream.ToArray(), DataPackLimits.Default.FileSize).File;
    }

    private static NbtList Ints(string name, params int[] values)
    {
        var list = new NbtList(NbtTagType.Int, name);
        foreach (var value in values)
            list.Add(new NbtTag<int>("", value));

        return list;
    }

    private static int DiamondBlocks(IChunk chunk) =>
        Enumerable.Range(0, 16 * 16 * 384)
            .Count(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15).Is(BlockIds.DiamondBlock));
}

using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Structures;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Generators.Mojang.Structures;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Structure and pool element types plugins register in code.
/// </summary>
/// <remarks>
/// The types are registered from the test's own load context, as <see cref="PluginFeatureFiles"/> does. These tests
/// run with the other tests applying plugins' tags and structures.
/// </remarks>
[Trait("Category", TestCategory.Worldgen)]
[Collection(nameof(PluginTagFiles))]
public class PluginStructureTypes
{
    internal const long Seed = 1234L;

    internal const string Spire = "structuretest:spire";

    // A spire of the plugin's type in every overworld biome, five blocks tall.
    private const string SpireStructure = """
        {
          "type": "structuretest:spire",
          "biomes": "#minecraft:is_overworld",
          "spawn_overrides": {},
          "step": "surface_structures",
          "height": 5
        }
        """;

    // A start in every chunk of even coordinates.
    private const string Spires = """
        {
          "structures": [{ "structure": "structuretest:spire", "weight": 1 }],
          "placement": { "type": "minecraft:random_spread", "spacing": 2, "separation": 1, "salt": 1 }
        }
        """;

    private const string Pillar = "structuretest:pillar";

    // A pillar element of the plugin's type, with a cap of another attached to its top jigsaw.
    private const string PillarStructure = """
        {
          "type": "minecraft:jigsaw",
          "biomes": "#minecraft:is_overworld",
          "spawn_overrides": {},
          "step": "surface_structures",
          "start_pool": "structuretest:pillar/start",
          "size": 1,
          "start_height": { "absolute": 0 },
          "project_start_to_heightmap": "WORLD_SURFACE_WG",
          "max_distance_from_center": 16,
          "use_expansion_hack": false
        }
        """;

    private const string PillarPool = """
        {
          "fallback": "minecraft:empty",
          "elements": [
            {
              "weight": 1,
              "element": { "element_type": "structuretest:pillar", "projection": "rigid", "height": 4 }
            }
          ]
        }
        """;

    private const string CapPool = """
        {
          "fallback": "minecraft:empty",
          "elements": [{ "weight": 1, "element": { "element_type": "structuretest:cap" } }]
        }
        """;

    private const string Pillars = """
        {
          "structures": [{ "structure": "structuretest:pillar", "weight": 1 }],
          "placement": { "type": "minecraft:random_spread", "spacing": 2, "separation": 1, "salt": 2 }
        }
        """;

    [Theory(DisplayName = "A plugin's structure type starts and places its pieces, and goes with the plugin")]
    [InlineData(true)]
    public void GeneratesPluginStructureTypes(bool current)
    {
        var logger = new PluginFeatureFiles.ListLogger();
        var built = Build(SpireFiles(), Types(), logger, current);
        Assert.Empty(logger.Messages);

        // Obsidian reads the fields every structure has, and the plugin's type the rest.
        var spire = Assert.IsType<PluginStructure>(built.Structures[Spire]);
        Assert.Equal(DecorationStep.SurfaceStructures, spire.Step);
        Assert.Equal(5, Assert.IsType<SpireType>(spire.Definition).Height);

        var structures = Only(built, "structuretest:spires");
        var builder = NativeChunks.Pipeline(Seed, () => structures);
        var chunks = new NativeChunks(builder);
        var manager = builder.Structures!;

        var start = manager.GetStarts(0, 0).Single(candidate => candidate.Structure.Identifier == Spire);
        Assert.Equal(5, Count(chunks.Get(0, 0), BlockIds.EmeraldBlock));

        StructureManager.RestoreStart(start, null);
        var saved = manager.SaveStarts(0, 0, null);

        // The plugin unloads: its type is gone, so its structure is left out like a file naming any missing type,
        // with the set naming it, and chunks generated later don't get it.
        structures = Only(Build(SpireFiles(), [], logger, current));

        Assert.Contains(logger.Messages, message => message.Contains("structuretest:spire isn't a structure type"));
        Assert.False(structures.Structures.ContainsKey(Spire));
        Assert.Equal(0, Count(chunks.Get(8, 8), BlockIds.EmeraldBlock));

        // After a restart without the plugin, the chunk's saved start loads and stays as it was saved.
        var restarted = NativeChunks.Pipeline(Seed, () => structures).Structures!;
        foreach (var candidate in restarted.GetStarts(0, 0))
            StructureManager.RestoreStart(candidate, saved);

        Assert.True(restarted.SaveStarts(0, 0, saved)!.TryGetTag<NbtCompound>(Spire, out _));
    }

    [Theory(DisplayName = "A plugin's pool element types are placed by template pools, and connect by their jigsaws")]
    [InlineData(true)]
    public void PlacesPluginPoolElements(bool current)
    {
        var logger = new PluginFeatureFiles.ListLogger();
        var built = Build(PillarFiles(), Types(), logger, current);
        Assert.Empty(logger.Messages);

        var element = Assert.IsType<PluginPoolElement>(Assert.Single(built.Pools["structuretest:pillar/start"].Elements)
            .Element);
        Assert.Equal(4, Assert.IsType<PillarElement>(element.Element).Height);

        var structures = Only(built, "structuretest:pillars");
        var builder = NativeChunks.Pipeline(Seed, () => structures);
        var chunks = new NativeChunks(builder);

        // The pillar, then the cap attached to its top jigsaw by the cap's default bottom one.
        var start = builder.Structures!.GetStarts(0, 0).Single(candidate => candidate.Structure.Identifier == Pillar);
        Assert.Equal(2, start.Pieces.Count);

        var chunk = chunks.Get(0, 0);
        Assert.Equal(4, Count(chunk, BlockIds.LapisBlock));
        Assert.Equal(1, Count(chunk, BlockIds.EmeraldBlock));

        // Without the plugin's types, the pools naming them are left out, with the structure starting from them.
        built = Build(PillarFiles(), [], logger, current);

        Assert.Contains(logger.Messages, message => message.Contains("structuretest:pillar isn't a type"));
        Assert.False(built.Structures.ContainsKey(Pillar));
    }

    private static WorldgenStructures Build(
        StructureFile[] files,
        List<WorldgenTypeRegistration> types,
        PluginFeatureFiles.ListLogger logger,
        bool current) => current
            ? StructureData.Read(new FeatureRegistry(WorldgenData.Vanilla), files, [], types, logger)
            : PluginStructures.Build(files, [], WorldgenFeatureSet.Vanilla, types, logger);

    internal static List<WorldgenTypeRegistration> Types()
    {
        List<WorldgenTypeRegistration> types = [];
        types.Add(WorldgenTypeRegistration.Create(types, Spire, typeof(CustomStructure), typeof(SpireType)));
        types.Add(WorldgenTypeRegistration.Create(types, Pillar, typeof(CustomPoolElement), typeof(PillarElement)));
        types.Add(WorldgenTypeRegistration.Create(
            types,
            "structuretest:cap",
            typeof(CustomPoolElement),
            typeof(CapElement)));
        return types;
    }

    internal static StructureFile[] SpireFiles() =>
    [
        Parse(PluginStructures.Structure, Spire, SpireStructure),
        Parse(PluginStructures.StructureSet, "structuretest:spires", Spires)
    ];

    private static StructureFile[] PillarFiles() =>
    [
        Parse(PluginStructures.Structure, Pillar, PillarStructure),
        Parse(PluginStructures.TemplatePool, "structuretest:pillar/start", PillarPool),
        Parse(PluginStructures.TemplatePool, "structuretest:pillar/caps", CapPool),
        Parse(PluginStructures.StructureSet, "structuretest:pillars", Pillars)
    ];

    private static StructureFile Parse(string registry, string identifier, string json) =>
        PluginStructures.Parse(registry, identifier, "test", Encoding.UTF8.GetBytes(json));

    // Only some of the structure sets, so that starts aren't made for vanilla's others.
    internal static WorldgenStructures Only(WorldgenStructures structures, params string[] sets) => structures with
    {
        Sets = structures.Sets.Where(set => sets.Contains(set.Key)).ToDictionary()
    };

    private static int Count(IChunk chunk, int blockId) =>
        Enumerable.Range(0, 16 * 16 * 384)
            .Count(i => chunk.GetBlock(i & 15, (i >> 8) - 64, (i >> 4) & 15).Is(blockId));

    // A column of emerald blocks of its height, at the middle of its chunk.
    private sealed class SpireType : CustomStructure
    {
        public required int Height { get; init; }

        public override Vector? FindStart(StructureStartContext context)
        {
            var x = (context.ChunkX << 4) + 8;
            var z = (context.ChunkZ << 4) + 8;
            return new Vector(x, context.GetBaseHeight(x, z, HeightmapType.WorldSurfaceWG), z);
        }

        public override void AddPieces(
            StructureStartContext context,
            Vector start,
            ICollection<CustomStructurePiece> pieces) =>
            pieces.Add(new SpirePiece(new BlockArea(start, start + new Vector(0, this.Height - 1, 0))));
    }

    private sealed class SpirePiece(BlockArea bounds) : CustomStructurePiece(bounds)
    {
        public override void Place(CustomPieceContext context)
        {
            for (var y = 0; y <= this.Bounds.Max.Y - this.Bounds.Min.Y; y++)
                context.SetBlock(new Vector(0, y, 0), BlocksRegistry.EmeraldBlock);
        }
    }

    // A column of lapis blocks with a jigsaw on top, which attaches a cap from the caps pool.
    private sealed class PillarElement : CustomPoolElement
    {
        public required int Height { get; init; }

        public override Vector Size => new(1, this.Height, 1);

        public override IReadOnlyList<PoolJigsaw> GetJigsaws() =>
        [
            new(
                new Vector(0, this.Height - 1, 0),
                "up_north",
                "structuretest:top",
                Target: "minecraft:bottom",
                Pool: "structuretest:pillar/caps")
        ];

        public override bool Place(CustomPieceContext context)
        {
            for (var y = 0; y < this.Height; y++)
                context.SetBlock(new Vector(0, y, 0), BlocksRegistry.LapisBlock);

            return true;
        }
    }

    // One emerald block, hanging off its default bottom jigsaw.
    private sealed class CapElement : CustomPoolElement
    {
        public override bool Place(CustomPieceContext context) =>
            context.SetBlock(Vector.Zero, BlocksRegistry.EmeraldBlock);
    }
}

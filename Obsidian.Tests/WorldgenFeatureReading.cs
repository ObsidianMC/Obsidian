using Obsidian.API;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Structures;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Pools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The runtime worldgen reader, which reads plugins' features.
/// </summary>
public class WorldgenFeatureReading
{
    [Fact(DisplayName = "A plugin's tree part and structure processor types are read in vanilla features")]
    public void ReadsPluginTreePartsAndProcessors()
    {
        List<WorldgenTypeRegistration> registrations = [];
        registrations.Add(WorldgenTypeRegistration.Create(
            registrations,
            "probe:cube",
            typeof(FoliagePlacer),
            typeof(CubeFoliagePlacer)));
        registrations.Add(WorldgenTypeRegistration.Create(
            registrations,
            "probe:cap",
            typeof(TreeDecorator),
            typeof(CapDecorator)));
        registrations.Add(WorldgenTypeRegistration.Create(
            registrations,
            "probe:column",
            typeof(FeatureSize),
            typeof(ColumnFeatureSize)));
        registrations.Add(WorldgenTypeRegistration.Create(
            registrations,
            "probe:decay",
            typeof(StructureProcessor),
            typeof(DecayProcessor)));

        var reader = new WorldgenReader(
            WorldgenTypes.With(registrations),
            id => throw new InvalidDataException(id),
            id => throw new InvalidDataException(id));
        using var document = JsonDocument.Parse("""
            {
              "type": "minecraft:tree",
              "config": {
                "trunk_provider": {
                  "type": "minecraft:simple_state_provider",
                  "state": { "Name": "minecraft:oak_log" }
                },
                "trunk_placer": {
                  "type": "minecraft:straight_trunk_placer",
                  "base_height": 4,
                  "height_rand_a": 2,
                  "height_rand_b": 0
                },
                "foliage_provider": {
                  "type": "minecraft:simple_state_provider",
                  "state": { "Name": "minecraft:oak_leaves" }
                },
                "foliage_placer": { "type": "probe:cube", "radius": 2, "offset": 0, "height": 3 },
                "dirt_provider": { "type": "minecraft:simple_state_provider", "state": { "Name": "minecraft:dirt" } },
                "minimum_size": { "type": "probe:column", "min_clipped_height": 3 },
                "decorators": [{ "type": "probe:cap", "state": { "Name": "minecraft:gold_block" } }]
              }
            }
            """);

        var tree = Assert.IsType<TreeFeature>(reader.ReadConfiguredFeature(document.RootElement, "probe:tree"));

        Assert.Equal(3, Assert.IsType<CubeFoliagePlacer>(tree.FoliagePlacer).Height);
        Assert.Equal(2, tree.FoliagePlacer.Radius.MaxValue);
        Assert.Equal("minecraft:gold_block", Assert.IsType<CapDecorator>(Assert.Single(tree.Decorators)).State.Name);
        Assert.Equal(3, Assert.IsType<ColumnFeatureSize>(tree.MinimumSize).MinClippedHeight);

        using var fossilDocument = JsonDocument.Parse("""
            {
              "type": "minecraft:fossil",
              "config": {
                "fossil_structures": ["minecraft:fossil/spine_1"],
                "overlay_structures": ["minecraft:fossil/spine_1_coal"],
                "fossil_processors": { "processors": [{ "processor_type": "probe:decay", "integrity": 0.5 }] },
                "overlay_processors": { "processors": [] },
                "max_empty_corners_allowed": 4
              }
            }
            """);

        var fossil = Assert.IsType<FossilFeature>(reader.ReadConfiguredFeature(fossilDocument.RootElement));

        Assert.Equal(0.5f, Assert.IsType<DecayProcessor>(Assert.Single(fossil.FossilProcessors.Processors)).Integrity);
        Assert.Empty(fossil.OverlayProcessors.Processors);
    }

    // A plugin's foliage placer: square rows of leaves of its radius, for its height.
    public sealed class CubeFoliagePlacer : FoliagePlacer
    {
        public required int Height { get; init; }

        public override int GetFoliageHeight(IRandomSource random, int treeHeight) => this.Height;

        protected override void CreateFoliage(
            TreeContext tree,
            int freeTreeHeight,
            FoliageAttachment attachment,
            int foliageHeight,
            int foliageRadius,
            int offset)
        {
            for (var y = offset; y > offset - foliageHeight; y--)
                this.PlaceLeavesRow(tree, attachment.Position, foliageRadius, y, attachment.DoubleTrunk);
        }

        protected override bool ShouldSkipLocation(
            IRandomSource random,
            int dx,
            int y,
            int dz,
            int radius,
            bool doubleTrunk) => false;
    }

    // A plugin's tree decorator: a block on the highest log.
    public sealed class CapDecorator : TreeDecorator
    {
        public required SimpleBlockState State { get; init; }

        public override void Place(TreeDecoratorContext context)
        {
            if (context.Logs.Count > 0)
                context.SetBlock(context.Logs[^1] + Vector.Up, this.State.ToBlock());
        }
    }

    // A plugin's feature size: only the trunk's column needs to be free.
    public sealed class ColumnFeatureSize : FeatureSize
    {
        public override int GetSizeAtHeight(int height, int y) => 0;
    }

    // A plugin's structure processor: keeps each template block with a chance of its integrity.
    public sealed class DecayProcessor : StructureProcessor
    {
        public required float Integrity { get; init; }

        public override StructureBlockInfo? ProcessBlock(
            IWorldGenLevel level,
            Vector origin,
            Vector pivot,
            StructureBlockInfo original,
            StructureBlockInfo current,
            TemplatePlacement placement) =>
            placement.GetRandom(current.Position).NextFloat() < this.Integrity ? current : null;
    }
}

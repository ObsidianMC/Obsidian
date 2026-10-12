using Obsidian.API;
using Obsidian.API.World.Features;
using Obsidian.API.World.Features.Trees;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Structures;
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
/// The runtime worldgen reader, which reads plugins' features, against the source generator's vanilla features.
/// </summary>
public class WorldgenFeatureReading
{
    [Fact(DisplayName = "Every vanilla feature read at runtime equals the generated one")]
    public void ReadsVanillaFeaturesLikeTheGenerator()
    {
        var reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            id => ConfiguredFeatures.All.GetValueOrDefault(id) ?? throw new InvalidDataException(id),
            id => PlacedFeatures.All.GetValueOrDefault(id) ?? throw new InvalidDataException(id));

        var differences = new List<string>();
        var configured = 0;
        var placed = 0;

        foreach (var (id, json) in Entries("worldgen.features."))
        {
            Compare(ConfiguredFeatures.All[id], reader.ReadConfiguredFeature(json, id), id, differences, 0);
            configured++;
        }

        foreach (var (id, json) in Entries("worldgen.placed_features."))
        {
            Compare(PlacedFeatures.All[id], reader.ReadPlacedFeature(json, id), id, differences, 0);
            placed++;
        }

        Assert.Empty(differences.Take(20));
        Assert.Equal(ConfiguredFeatures.All.Count, configured);
        Assert.Equal(PlacedFeatures.All.Count, placed);
    }

    [Fact(DisplayName = "A plugin's tree part types are read in a vanilla tree")]
    public void ReadsPluginTreeParts()
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

    // The entries of the category assets the generator reads, like Assets/worldgen/features/ores.json.
    private static IEnumerable<(string Id, JsonElement Json)> Entries(string folder)
    {
        var assembly = typeof(ConfiguredFeatures).Assembly;
        var prefix = $"Obsidian.Assets.{folder}";

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var document = JsonDocument.Parse(stream);

            foreach (var entry in document.RootElement.EnumerateObject())
                yield return (entry.Name, entry.Value.Clone());
        }
    }

    // Compares two object graphs: the same types and property values, collections item by item, and named registry
    // entries below the root by identifier.
    private static void Compare(object? expected, object? actual, string path, List<string> differences, int depth)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
                differences.Add($"{path}: {expected ?? "null"} != {actual ?? "null"}");

            return;
        }

        if (depth > 0 && NamedEntry(expected) is string expectedId)
        {
            if (NamedEntry(actual) != expectedId)
                differences.Add($"{path}: refers to {NamedEntry(actual) ?? "an inline entry"}, not {expectedId}");

            return;
        }

        var type = expected.GetType();
        if (type.IsPrimitive || type.IsEnum || expected is string or decimal)
        {
            if (!expected.Equals(actual))
                differences.Add($"{path}: {expected} != {actual}");

            return;
        }

        if (expected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
        {
            if (expectedDictionary.Count != actualDictionary.Count)
                differences.Add($"{path}: {expectedDictionary.Count} entries != {actualDictionary.Count}");

            foreach (DictionaryEntry entry in expectedDictionary)
                Compare(entry.Value, actualDictionary[entry.Key], $"{path}[{entry.Key}]", differences, depth + 1);

            return;
        }

        // Collections of any kind compare by items, as the generator's collection expressions pick their own types.
        if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
        {
            var left = expectedItems.Cast<object?>().ToList();
            var right = actualItems.Cast<object?>().ToList();

            if (left.Count != right.Count)
                differences.Add($"{path}: {left.Count} items != {right.Count}");

            for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
                Compare(left[i], right[i], $"{path}[{i}]", differences, depth + 1);

            return;
        }

        if (actual.GetType() != type)
        {
            differences.Add($"{path}: {type.Name} != {actual.GetType().Name}");
            return;
        }

        foreach (var property in StateProperties(type))
        {
            Compare(
                property.GetValue(expected),
                property.GetValue(actual),
                $"{path}.{property.Name}",
                differences,
                depth + 1);
        }
    }

    private static string? NamedEntry(object value) => value switch
    {
        ConfiguredFeatureBase { Identifier.Length: > 0 } feature => feature.Identifier,
        PlacedFeature { Identifier.Length: > 0 } feature => feature.Identifier,
        StructureProcessorList { Identifier.Length: > 0 } list => list.Identifier,
        _ => null
    };

    // Public properties, and other ones data can set.
    private static IEnumerable<PropertyInfo> StateProperties(Type type) => type
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
        .Where(property => property.GetMethod!.IsPublic || property.SetMethod is not null);
}

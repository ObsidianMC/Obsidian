using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Registries;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The runtime reader of noise settings, density functions and noises, which reads plugins' files, against the
/// source generator's vanilla entries.
/// </summary>
public class WorldgenNoiseReading
{
    [Fact(DisplayName = "Vanilla's noise entries read at runtime equal the generated ones, shared references included")]
    public void ReadsVanillaNoiseLikeTheGenerator()
    {
        var read = ReadVanillaFiles(out var logger);
        Assert.Empty(logger.Messages);

        // Registry entries referred to by identifier are one shared instance, in the generated code as when read.
        var named = NoiseRegistry.DensityFunctions.All.ToDictionary(
            entry => (object)entry.Value,
            entry => entry.Key,
            ReferenceEqualityComparer.Instance);

        var comparer = new GraphComparer(read, named);

        foreach (var (id, noise) in NoiseRegistry.Noises.All)
            comparer.Compare(noise, read.Noises.GetValueOrDefault(id), id, root: true);

        foreach (var (id, function) in NoiseRegistry.DensityFunctions.All)
            comparer.Compare(function, read.DensityFunctions.GetValueOrDefault(id), id, root: true);

        foreach (var (id, settings) in NoiseRegistry.NoiseSettings.All)
            comparer.Compare(settings, read.NoiseSettings.GetValueOrDefault(id), id, root: true);

        Assert.Empty(comparer.Differences.Take(20));
        Assert.Equal(NoiseRegistry.Noises.All.Count, read.Noises.Count);
        Assert.Equal(NoiseRegistry.DensityFunctions.All.Count, read.DensityFunctions.Count);
        Assert.Equal(NoiseRegistry.NoiseSettings.All.Count, read.NoiseSettings.Count);
    }

    /// <summary>
    /// Vanilla's noise settings, density function and noise files, the generator's inputs, read at runtime without
    /// the generated entries, so that every reference is to an entry read too.
    /// </summary>
    internal static WorldgenNoise ReadVanillaFiles(out PluginFeatureFiles.ListLogger logger)
    {
        var assembly = typeof(PluginNoise).Assembly;
        var files = new List<NoiseFile>();

        foreach (var registry in PluginNoise.Registries)
        {
            var prefix = $"Obsidian.Assets.{registry.Replace('/', '.')}.";

            foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix)))
            {
                // Resource names have dots for the folders of identifiers like overworld/caves/entrances.
                var path = resource[prefix.Length..^".json".Length].Replace('.', '/');

                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);

                files.Add(PluginNoise.Create(registry, $"minecraft:{path}", resource, memory.ToArray()));
            }
        }

        logger = new PluginFeatureFiles.ListLogger();
        return PluginNoise.Build(files, [], logger, builtIn: false);
    }

    // Compares the generated graphs with the read ones: the same types and property values, collections item by
    // item, and registry entries below the root by identity with the read entry of the same identifier.
    private sealed class GraphComparer(WorldgenNoise read, Dictionary<object, string> named)
    {
        private readonly HashSet<(object, object)> compared = new(new PairComparer());

        public List<string> Differences { get; } = [];

        public void Compare(object? expected, object? actual, string path, bool root = false)
        {
            if (expected is null || actual is null)
            {
                if (expected is not null || actual is not null)
                    this.Differences.Add($"{path}: {expected ?? "null"} != {actual ?? "null"}");

                return;
            }

            if (!root && named.TryGetValue(expected, out var function))
            {
                if (!ReferenceEquals(actual, read.DensityFunctions.GetValueOrDefault(function)))
                    this.Differences.Add($"{path}: isn't the shared {function}");

                return;
            }

            if (!root && expected is BaseNoise noise)
            {
                if (!ReferenceEquals(actual, read.Noises.GetValueOrDefault(noise.Key)))
                    this.Differences.Add($"{path}: isn't the shared {noise.Key}");

                return;
            }

            var type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum || expected is string or decimal)
            {
                if (!expected.Equals(actual))
                    this.Differences.Add($"{path}: {expected} != {actual}");

                return;
            }

            if (!type.IsValueType && !this.compared.Add((expected, actual)))
                return;

            if (expected is IDictionary expectedDictionary && actual is IDictionary actualDictionary)
            {
                if (expectedDictionary.Count != actualDictionary.Count)
                    this.Differences.Add($"{path}: {expectedDictionary.Count} entries != {actualDictionary.Count}");

                foreach (DictionaryEntry entry in expectedDictionary)
                    this.Compare(entry.Value, actualDictionary[entry.Key], $"{path}[{entry.Key}]");

                return;
            }

            // Collections compare by items, as the generator's collection expressions pick their own types.
            if (expected is IEnumerable expectedItems && actual is IEnumerable actualItems)
            {
                var left = expectedItems.Cast<object?>().ToList();
                var right = actualItems.Cast<object?>().ToList();

                if (left.Count != right.Count)
                    this.Differences.Add($"{path}: {left.Count} items != {right.Count}");

                for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
                    this.Compare(left[i], right[i], $"{path}[{i}]");

                return;
            }

            if (actual.GetType() != type)
            {
                this.Differences.Add($"{path}: {type.Name} != {actual.GetType().Name}");
                return;
            }

            foreach (var property in DataProperties(type))
                this.Compare(property.GetValue(expected), property.GetValue(actual), $"{path}.{property.Name}");
        }

        // The properties data sets; the others are computed from them.
        private static IEnumerable<PropertyInfo> DataProperties(Type type) => type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.GetIndexParameters().Length == 0 && property.SetMethod is not null);

        private sealed class PairComparer : IEqualityComparer<(object, object)>
        {
            public bool Equals((object, object) x, (object, object) y) =>
                ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);

            public int GetHashCode((object, object) pair) => HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item1),
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Item2));
        }
    }
}

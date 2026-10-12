using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// What plugins' and data packs' files share when they define the same entries, vanilla's included: the last file in
/// order wins, whole, like vanilla's data packs.
/// </summary>
/// <remarks>
/// Vanilla's worldgen entries are compiled, each holding the entries it names. An override is only read at runtime,
/// so the vanilla entries naming it, directly or through others, are read again from vanilla's own files with the
/// runtime reader (see <see cref="Dependents"/>), which reads them into the same objects as the compiled ones.
/// Vanilla's entries no override reaches keep their compiled instances.
/// </remarks>
internal static partial class VanillaOverrides
{
    // Where the source generator's inputs for each registry are embedded, and whether each file holds a whole category
    // of entries by identifier (Assets/worldgen/features/ores.json) rather than one entry.
    private static readonly FrozenDictionary<string, (string Prefix, bool Grouped)> resources =
        new Dictionary<string, (string Prefix, bool Grouped)>
        {
            [PluginNoise.NoiseSettings] = ("Obsidian.Assets.worldgen.noise_settings.", false),
            [PluginNoise.DensityFunction] = ("Obsidian.Assets.worldgen.density_function.", false),
            [PluginNoise.Noise] = ("Obsidian.Assets.worldgen.noise.", false),
            [PluginFeatures.ConfiguredFeature] = ("Obsidian.Assets.worldgen.features.", true),
            [PluginFeatures.PlacedFeature] = ("Obsidian.Assets.worldgen.placed_features.", true),
            [PluginFeatures.ProcessorList] = ("Obsidian.Assets.worldgen.processor_lists.", true),
            [PluginStructures.Structure] = ("Obsidian.Assets.worldgen.structures.", true),
            [PluginStructures.StructureSet] = ("Obsidian.Assets.worldgen.structure_sets.", true),
            [PluginStructures.TemplatePool] = ("Obsidian.Assets.worldgen.template_pools.", true)
        }.ToFrozenDictionary();

    private static readonly ConcurrentDictionary<string, FrozenDictionary<string, JsonElement>> vanilla = new();

    /// <summary>
    /// Keeps, of the files defining each entry, the last in order, in the order entries were first defined. Each
    /// replaced file is logged.
    /// </summary>
    /// <param name="files">Plugins' files in load order, then data packs' in theirs.</param>
    public static List<T> LastWins<T>(
        IEnumerable<T> files,
        Func<T, (string Registry, string Identifier)> entry,
        Func<T, string> source,
        ILogger logger)
    {
        var byEntry = new OrderedDictionary<(string Registry, string Identifier), T>();

        foreach (var file in files)
        {
            var key = entry(file);
            if (byEntry.TryGetValue(key, out var replaced))
            {
                var winner = source(file);
                var loser = source(replaced);
                Log.Replaced(logger, key.Identifier, winner, loser);
            }

            byEntry[key] = file;
        }

        return [.. byEntry.Values];
    }

    /// <summary>
    /// Vanilla's entries of a registry, as the JSON the source generator reads, by identifier.
    /// </summary>
    /// <param name="registry">A noise, feature or structure registry, like <see cref="PluginNoise.DensityFunction"/>.
    /// </param>
    public static FrozenDictionary<string, JsonElement> Entries(string registry) =>
        vanilla.GetOrAdd(registry, Load);

    /// <summary>
    /// Vanilla's entries of <paramref name="registries"/> that name an entry of <paramref name="overridden"/>,
    /// directly or through other vanilla entries, and so have to be read again; those overridden themselves aren't.
    /// </summary>
    /// <remarks>
    /// References are found as any string in an entry's JSON equal to an identifier, which may take a block's name
    /// for an entry's: an entry read again needlessly is still the same as the compiled one.
    /// </remarks>
    /// <param name="overridden">The identifiers files define, in any of <paramref name="registries"/>.</param>
    public static List<(string Registry, string Identifier, JsonElement Json)> Dependents(
        IEnumerable<string> registries,
        IReadOnlySet<(string Registry, string Identifier)> overridden)
    {
        var entries = registries
            .SelectMany(registry => Entries(registry).Select(entry => (registry, entry.Key, entry.Value)))
            .ToList();

        // Which entries name each identifier.
        var namedBy = new Dictionary<string, List<int>>();

        for (var i = 0; i < entries.Count; i++)
        {
            foreach (var name in Strings(entries[i].Value).Distinct())
            {
                if (!namedBy.TryGetValue(name, out var list))
                    namedBy[name] = list = [];

                list.Add(i);
            }
        }

        var queue = new Queue<string>(overridden.Select(entry => entry.Identifier).Distinct());
        var queued = queue.ToHashSet();
        var reached = new SortedSet<int>();

        while (queue.TryDequeue(out var identifier))
        {
            foreach (var i in namedBy.GetValueOrDefault(identifier) ?? [])
            {
                if (reached.Add(i) && queued.Add(entries[i].Key))
                    queue.Enqueue(entries[i].Key);
            }
        }

        return [.. reached
            .Select(i => entries[i])
            .Where(entry => !overridden.Contains((entry.registry, entry.Key)))];
    }

    // Every string value in a JSON tree.
    private static IEnumerable<string> Strings(JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.String:
                yield return json.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (var item in json.EnumerateArray().SelectMany(Strings))
                    yield return item;

                break;
            case JsonValueKind.Object:
                foreach (var item in json.EnumerateObject().SelectMany(property => Strings(property.Value)))
                    yield return item;

                break;
        }
    }

    private static FrozenDictionary<string, JsonElement> Load(string registry)
    {
        var (prefix, grouped) = resources[registry];
        var assembly = typeof(VanillaOverrides).Assembly;
        var entries = new Dictionary<string, JsonElement>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing resource {resource}.");
            using var document = JsonDocument.Parse(stream);

            if (grouped)
            {
                foreach (var entry in document.RootElement.EnumerateObject())
                    entries[entry.Name] = entry.Value.Clone();

                continue;
            }

            // Resource names have dots for the folders of identifiers like overworld/caves/entrances.
            var path = resource[prefix.Length..^".json".Length].Replace('.', '/');
            entries[$"minecraft:{path}"] = document.RootElement.Clone();
        }

        return entries.ToFrozenDictionary();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "{Identifier} from {Source} replaces the one from {Replaced}")]
        public static partial void Replaced(ILogger logger, string identifier, string source, string replaced);
    }
}

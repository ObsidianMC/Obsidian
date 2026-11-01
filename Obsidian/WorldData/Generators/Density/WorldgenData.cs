using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// The 26.3 worldgen JSON used by density and terrain: noises, functions, settings, material rules and conditions,
/// carvers, biome inputs and block tags, read by registry and identifier.
/// </summary>
/// <remarks>
/// <see cref="Vanilla"/> reads the files Obsidian.AssetGenerator copies to <c>Assets/worldgen_26_3</c>. Temporary
/// beside the legacy worldgen assets, which the live generator still reads (Phase 5 of
/// <c>docs/porting-26.3-plan.md</c>).
/// </remarks>
/// <param name="open">Opens an entry's JSON by registry and identifier, or gives <c>null</c>.</param>
/// <param name="list">The identifiers of a registry's entries; none by default.</param>
internal sealed class WorldgenData(Func<string, string, Stream?> open, Func<string, IEnumerable<string>>? list = null)
{
    private const string ResourcePrefix = "Obsidian.Assets.worldgen_26_3.";

    private readonly ConcurrentDictionary<(string Registry, string Id), JsonElement> entries = new();

    // Entries laid over the opened ones (see With), by registry and qualified identifier; none for opened data.
    private readonly FrozenDictionary<(string Registry, string Id), JsonElement> layered =
        FrozenDictionary<(string Registry, string Id), JsonElement>.Empty;

    private WorldgenData(
        WorldgenData below,
        FrozenDictionary<(string Registry, string Id), JsonElement> layered)
        : this(below.OpenBelow, below.Ids)
    {
        this.layered = layered;
    }

    /// <summary>Vanilla's entries, embedded in Obsidian.</summary>
    public static WorldgenData Vanilla { get; } = new(OpenEmbedded, ListEmbedded);

    /// <summary>Whether entries were laid over the data this was made from (see <see cref="With"/>).</summary>
    public bool IsLayered => this.layered.Count > 0;

    /// <summary>
    /// The identifiers of a registry's entries, in no particular order, for registries read whole (structures).
    /// </summary>
    public IEnumerable<string> Ids(string registry)
    {
        var below = list?.Invoke(registry) ?? [];
        if (this.layered.Count == 0)
            return below;

        var above = this.layered.Keys
            .Where(key => key.Registry == registry)
            .Select(key => key.Id);

        return below.Concat(above).Distinct();
    }

    /// <summary>
    /// An entry's JSON, such as <c>Read("noise", "minecraft:temperature")</c>. An identifier without a namespace is
    /// vanilla's, as <c>Read("noise", "temperature")</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">There is no such entry.</exception>
    public JsonElement Read(string registry, string id)
    {
        var key = (registry, Qualify(id));

        if (this.layered.TryGetValue(key, out var entry))
            return entry;

        return this.entries.GetOrAdd(key, static (key, open) => Parse(open, key.Registry, key.Id), open);
    }

    /// <summary>
    /// This data with entries laid over it, such as plugins' and data packs' files: an entry of the same registry and
    /// identifier as one of this data's replaces it, wherever it's read or named, and the others are added. Entries
    /// name each other by identifier, so an entry replaced here changes every entry naming it.
    /// </summary>
    /// <param name="entries">The entries, by registry (as <see cref="Read"/> names them, like
    /// <c>density_function</c>) and identifier; when two have the same, the last wins.</param>
    public WorldgenData With(IEnumerable<WorldgenEntry> entries)
    {
        var layered = new Dictionary<(string Registry, string Id), JsonElement>(this.layered);

        foreach (var entry in entries)
            layered[(entry.Registry, Qualify(entry.Id))] = entry.Json.Clone();

        return layered.Count == 0 ? this : new WorldgenData(this, layered.ToFrozenDictionary());
    }

    // Reads what this data opens, below its own layered entries, for data laid over it.
    private Stream? OpenBelow(string registry, string id)
    {
        if (this.layered.TryGetValue((registry, id), out var entry))
            return new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(entry));

        return open(registry, id);
    }

    /// <summary>
    /// An identifier with its namespace, which is vanilla's when it has none, as vanilla's codecs read identifiers:
    /// data packs and the JSON typed into commands may write <c>stone</c> (or <c>:stone</c>) for
    /// <c>minecraft:stone</c>.
    /// </summary>
    public static string Qualify(string id)
    {
        var separator = id.IndexOf(':');

        // Vanilla's identifiers take an empty namespace, as in ":stone", for its own too.
        if (separator == 0)
            return $"minecraft{id}";

        return separator > 0 ? id : $"minecraft:{id}";
    }

    private static JsonElement Parse(Func<string, string, Stream?> open, string registry, string id)
    {
        using var stream = open(registry, id)
            ?? throw new InvalidDataException($"There is no {registry} {id}.");
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }

    // Vanilla's paths have no dots, so each dot of a resource name between the registry and ".json" is a folder.
    private static IEnumerable<string> ListEmbedded(string registry)
    {
        var prefix = $"{ResourcePrefix}{registry}.";
        const string suffix = ".json";

        return Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
            .Select(name => "minecraft:" + name[prefix.Length..^suffix.Length].Replace('.', '/'))
            .ToArray();
    }

    // Embedded resources are named by their folders, so minecraft:overworld/continents is
    // Obsidian.Assets.worldgen_26_3.density_function.overworld.continents.json. Only vanilla's namespace is copied.
    private static Stream? OpenEmbedded(string registry, string id)
    {
        const string vanillaNamespace = "minecraft:";
        if (!id.StartsWith(vanillaNamespace, StringComparison.Ordinal))
            return null;

        var path = id[vanillaNamespace.Length..].Replace('/', '.');
        var name = $"{ResourcePrefix}{registry}.{path}.json";

        return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
    }
}

/// <summary>An entry to lay over <see cref="WorldgenData"/> (see <see cref="WorldgenData.With"/>).</summary>
/// <param name="Registry">The registry as <see cref="WorldgenData.Read"/> names it, like <c>noise_settings</c>.</param>
/// <param name="Id">The entry's identifier.</param>
/// <param name="Json">The entry's JSON.</param>
internal readonly record struct WorldgenEntry(string Registry, string Id, JsonElement Json);

using System.Collections.Concurrent;
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

    /// <summary>Vanilla's entries, embedded in Obsidian.</summary>
    public static WorldgenData Vanilla { get; } = new(OpenEmbedded, ListEmbedded);

    /// <summary>
    /// The identifiers of a registry's entries, in no particular order, for registries read whole (structures).
    /// </summary>
    public IEnumerable<string> Ids(string registry) => list?.Invoke(registry) ?? [];

    /// <summary>
    /// An entry's JSON, such as <c>Read("noise", "minecraft:temperature")</c>. An identifier without a namespace is
    /// vanilla's, as <c>Read("noise", "temperature")</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">There is no such entry.</exception>
    public JsonElement Read(string registry, string id) =>
        this.entries.GetOrAdd((registry, Qualify(id)), static (key, open) => Parse(open, key.Registry, key.Id), open);

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

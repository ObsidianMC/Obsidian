using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.API.Registries;

// Looking tags up by identifier, and the tags plugins made, next to vanilla's generated ones.
public static partial class TagsRegistry
{
    private static readonly Lazy<FrozenDictionary<(string Registry, string Identifier), Tag>> vanilla = new(() => All
        .DistinctBy(tag => (tag.Registry, tag.Identifier))
        .ToFrozenDictionary(tag => (tag.Registry, tag.Identifier)));

    // The tags plugins made that vanilla doesn't have, by registry. Replaced whole when plugins load or unload.
    private static FrozenDictionary<string, Tag[]> pluginTags = FrozenDictionary<string, Tag[]>.Empty;

    private static int revision;

    /// <summary>
    /// How many times plugins' tags were applied. It changes once the tags (vanilla's included, whose entries plugins
    /// change) are rebuilt, so whatever caches tag contents can tell they're stale.
    /// </summary>
    public static int Revision => Volatile.Read(ref revision);

    /// <summary>
    /// Finds a tag, vanilla's or a plugin's, by its registry (like <c>block</c>) and identifier (like
    /// <c>minecraft:mineable/pickaxe</c>).
    /// </summary>
    public static bool TryGet(string registry, string identifier, [NotNullWhen(true)] out Tag? tag)
    {
        if (vanilla.Value.TryGetValue((registry, identifier), out tag))
            return true;

        tag = pluginTags.GetValueOrDefault(registry)?.FirstOrDefault(tag => tag.Identifier == identifier);
        return tag is not null;
    }

    /// <summary>
    /// Whether the entry with the given id in a tag's registry is in the tag; false if there's no such tag.
    /// </summary>
    public static bool Contains(string registry, string identifier, int id) =>
        TryGet(registry, identifier, out var tag) && tag.Entries.Contains(id);

    /// <summary>
    /// Every tag, by registry: vanilla's, with plugins' entries, and plugins' own.
    /// </summary>
    /// <remarks>
    /// Of the worldgen registries, vanilla syncs only biomes, so only their tags are here, as <c>worldgen/biome</c>.
    /// The server leaves out other registries clients don't have (like <c>villager_trade</c>) when it sends them.
    /// </remarks>
    internal static Dictionary<string, Tag[]> GetCategories()
    {
        var categories = Categories
            .Where(category => category.Key != "worldgen")
            .ToDictionary(category => category.Key, category => category.Value);

        categories["worldgen/biome"] = Worldgen.Biome.All;

        foreach (var (registry, tags) in pluginTags)
            categories[registry] = [.. categories.GetValueOrDefault(registry) ?? [], .. tags];

        return categories;
    }

    /// <summary>
    /// Vanilla's tag with the given identifier, whose entries plugins change in place.
    /// </summary>
    internal static bool TryGetVanilla(string registry, string identifier, [NotNullWhen(true)] out Tag? tag) =>
        vanilla.Value.TryGetValue((registry, identifier), out tag);

    /// <summary>
    /// The registries' vanilla tags.
    /// </summary>
    internal static IEnumerable<Tag> VanillaTagsOf(string registry) =>
        vanilla.Value.Values.Where(tag => tag.Registry == registry);

    /// <summary>
    /// Replaces the tags plugins made, once every tag was rebuilt, and moves <see cref="Revision"/> on.
    /// </summary>
    internal static void SetPluginTags(IReadOnlyDictionary<string, Tag[]> tags)
    {
        pluginTags = tags.ToFrozenDictionary();
        Interlocked.Increment(ref revision);
    }
}

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Registries;

// Looking tags up by identifier, and the tags plugins made, next to vanilla's generated ones.
public static partial class TagsRegistry
{
    private static readonly Lazy<FrozenDictionary<(string Registry, string Identifier), Tag>> vanilla = new(() => All
        .DistinctBy(tag => (tag.Registry, tag.Identifier))
        .ToFrozenDictionary(tag => (tag.Registry, tag.Identifier)));

    // The tags plugins made that vanilla doesn't have, by registry. Replaced whole when plugins load or unload.
    private static FrozenDictionary<string, Tag[]> pluginTags = FrozenDictionary<string, Tag[]>.Empty;

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
    /// Every tag clients are sent, by registry: vanilla's, with plugins' entries, and plugins' own.
    /// </summary>
    /// <remarks>
    /// Of the worldgen registries, vanilla syncs only biomes, so only their tags are sent, as <c>worldgen/biome</c>.
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
    /// Replaces the tags plugins made.
    /// </summary>
    internal static void SetPluginTags(IReadOnlyDictionary<string, Tag[]> tags) =>
        pluginTags = tags.ToFrozenDictionary();
}

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Obsidian.API.Registries;

/// <summary>
/// Vanilla's banner patterns (the <c>minecraft:banner_pattern</c> registry), numbered by identifier like a data
/// pack's registry, and the tags that say which the loom offers: <c>no_item_required</c>'s without a pattern item, a
/// <c>pattern_item/...</c> tag's with one.
/// </summary>
public static class BannerPatternsRegistry
{
    public const string CodecKey = "minecraft:banner_pattern";

    // The patterns a loom offers without a pattern item, in the tag's order, which is the loom's.
    private static readonly string[] noItemRequired =
    [
        "square_bottom_left", "square_bottom_right", "square_top_left", "square_top_right", "stripe_bottom",
        "stripe_top", "stripe_left", "stripe_right", "stripe_center", "stripe_middle", "stripe_downright",
        "stripe_downleft", "small_stripes", "cross", "straight_cross", "triangle_bottom", "triangle_top",
        "triangles_bottom", "triangles_top", "diagonal_left", "diagonal_up_right", "diagonal_up_left",
        "diagonal_right", "circle", "rhombus", "half_vertical", "half_horizontal", "half_vertical_right",
        "half_horizontal_bottom", "border", "gradient", "gradient_up"
    ];

    // Each pattern item's tag and the pattern it gives.
    private static readonly (Tag Tag, int Item, string Pattern)[] patternItems =
    [
        (TagsRegistry.BannerPattern.PatternItem.BordureIndented, ItemIds.BordureIndentedBannerPattern, "curly_border"),
        (TagsRegistry.BannerPattern.PatternItem.Creeper, ItemIds.CreeperBannerPattern, "creeper"),
        (TagsRegistry.BannerPattern.PatternItem.FieldMasoned, ItemIds.FieldMasonedBannerPattern, "bricks"),
        (TagsRegistry.BannerPattern.PatternItem.Flow, ItemIds.FlowBannerPattern, "flow"),
        (TagsRegistry.BannerPattern.PatternItem.Flower, ItemIds.FlowerBannerPattern, "flower"),
        (TagsRegistry.BannerPattern.PatternItem.Globe, ItemIds.GlobeBannerPattern, "globe"),
        (TagsRegistry.BannerPattern.PatternItem.Guster, ItemIds.GusterBannerPattern, "guster"),
        (TagsRegistry.BannerPattern.PatternItem.Mojang, ItemIds.MojangBannerPattern, "mojang"),
        (TagsRegistry.BannerPattern.PatternItem.Piglin, ItemIds.PiglinBannerPattern, "piglin"),
        (TagsRegistry.BannerPattern.PatternItem.Skull, ItemIds.SkullBannerPattern, "skull"),
    ];

    /// <summary>
    /// The patterns' identifiers, by id.
    /// </summary>
    public static ImmutableArray<string> Identifiers { get; }

    private static readonly FrozenDictionary<string, int> ids;

    static BannerPatternsRegistry()
    {
        Identifiers = [.. noItemRequired
            .Concat(patternItems.Select(item => item.Pattern))
            .Append("base")
            .Select(name => "minecraft:" + name)
            .Order(StringComparer.Ordinal)];
        ids = Identifiers.Select((name, id) => (name, id)).ToFrozenDictionary(pair => pair.name, pair => pair.id);

        // The generated tags have no entries, since the registry isn't among the generated ones.
        TagsRegistry.BannerPattern.NoItemRequired.SetEntries([.. noItemRequired.Select(name => IdOf("minecraft:" + name))]);
        foreach (var (tag, _, pattern) in patternItems)
            tag.SetEntries([IdOf("minecraft:" + pattern)]);
    }

    /// <summary>
    /// The pattern's id, or -1 for an unknown identifier.
    /// </summary>
    public static int IdOf(string identifier) => ids.GetValueOrDefault(identifier, -1);

    /// <summary>
    /// The patterns a loom offers with <paramref name="patternItem"/> in its pattern slot (null for none), in order.
    /// </summary>
    public static ImmutableArray<int> Selectable(int? patternItem)
    {
        if (patternItem is null)
            return TagsRegistry.BannerPattern.NoItemRequired.Entries;

        foreach (var (tag, item, _) in patternItems)
        {
            if (item == patternItem)
                return tag.Entries;
        }

        return [];
    }

    /// <summary>
    /// Whether the item provides banner patterns to a loom.
    /// </summary>
    public static bool IsPatternItem(int item) => patternItems.Any(entry => entry.Item == item);
}

namespace Obsidian.API.Blocks;

/// <summary>
/// The trees a sapling grows into, like vanilla's <c>TreeGrower</c>: configured features named by identifier, vanilla's
/// (<c>minecraft:oak</c>) or a plugin's or data pack's (from <c>worldgen/configured_feature</c> files), looked up when
/// the sapling grows, so that a reload or override applies at once.
/// </summary>
/// <remarks>
/// <para>
/// Growing picks like vanilla's since 26.1, each list by weight: a tree from <see cref="MegaTrees"/> where four
/// saplings of the block stand in a square (its north-west corner is where the feature's placed), else one from
/// <see cref="FlowerTrees"/> when a flower is within two blocks of the sapling, else one from <see cref="Trees"/>. A
/// sapling with nothing to place doesn't grow, like a single dark oak sapling, and bone meal does nothing to it.
/// </para>
/// <para>
/// A grower whose lists are all empty uses the older shape instead: <see cref="Tree"/>, <see cref="MegaTree"/> and
/// <see cref="Flowers"/>, each with its secondary tree taking <see cref="SecondaryChance"/> of the weight.
/// </para>
/// <para>
/// A sapling with a <c>stage</c> property (<c>StateProperty.Int("stage", 0, 1)</c>) grows in two steps like
/// vanilla's: the first sets its stage to 1, the second places the tree. Without one, every step places the tree.
/// </para>
/// </remarks>
public sealed record TreeGrower
{
    /// <summary>
    /// The trees it grows by itself, like an oak (nine in ten) or a fancy oak.
    /// </summary>
    public IReadOnlyList<WeightedFeature> Trees { get; init; } = [];

    /// <summary>
    /// The trees four saplings in a square grow into, like a dark oak.
    /// </summary>
    public IReadOnlyList<WeightedFeature> MegaTrees { get; init; } = [];

    /// <summary>
    /// The trees it grows near flowers, like an oak with a bee nest.
    /// </summary>
    public IReadOnlyList<WeightedFeature> FlowerTrees { get; init; } = [];

    /// <summary>
    /// The tree whose trunk bone meal needs room for: the sapling takes bone meal only where its trunk's base height
    /// above it is inside the build range.
    /// </summary>
    public string? ShortestTree { get; init; }

    /// <summary>
    /// The tree it grows by itself, in the older shape (see the remarks).
    /// </summary>
    public string? Tree { get; init; }

    /// <summary>
    /// The tree it grows instead of <see cref="Tree"/> with <see cref="SecondaryChance"/>, like a fancy oak.
    /// </summary>
    public string? SecondaryTree { get; init; }

    /// <summary>
    /// The tree four saplings in a square grow into, in the older shape.
    /// </summary>
    public string? MegaTree { get; init; }

    /// <summary>
    /// The tree four saplings grow into instead of <see cref="MegaTree"/> with <see cref="SecondaryChance"/>, like a
    /// mega pine.
    /// </summary>
    public string? SecondaryMegaTree { get; init; }

    /// <summary>
    /// The tree it grows near flowers, in the older shape.
    /// </summary>
    public string? Flowers { get; init; }

    /// <summary>
    /// The tree it grows near flowers instead of <see cref="Flowers"/> with <see cref="SecondaryChance"/>.
    /// </summary>
    public string? SecondaryFlowers { get; init; }

    /// <summary>
    /// How likely a secondary tree is, from 0 to 1.
    /// </summary>
    public float SecondaryChance { get; init; }
}

/// <summary>
/// A configured feature, by identifier, and its weight in a list picked from by weight.
/// </summary>
/// <param name="Feature">The configured feature's identifier, like <c>minecraft:oak</c>.</param>
/// <param name="Weight">Its weight, at least 1.</param>
public sealed record WeightedFeature(string Feature, int Weight);

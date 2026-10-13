namespace Obsidian.API.Blocks;

/// <summary>
/// The trees a sapling grows into, like vanilla's <c>TreeGrower</c>: configured features named by identifier, vanilla's
/// (<c>minecraft:oak</c>) or a plugin's or data pack's (from <c>worldgen/configured_feature</c> files), looked up when
/// the sapling grows, so that a reload or override applies at once.
/// </summary>
/// <remarks>
/// <para>
/// Growing picks like vanilla's: <see cref="MegaTree"/> where four saplings of the block stand in a square (its
/// north-west corner is where the feature's placed), else <see cref="Flowers"/> when a flower is within two blocks
/// of the sapling, else <see cref="Tree"/>. With <see cref="SecondaryChance"/>, the secondary feature of the choice is
/// placed instead when there's one. A sapling with nothing to place doesn't grow, like a single dark oak sapling.
/// </para>
/// <para>
/// A sapling with a <c>stage</c> property (<c>StateProperty.Int("stage", 0, 1)</c>) grows in two steps like
/// vanilla's: the first sets its stage to 1, the second places the tree. Without one, every step places the tree.
/// </para>
/// </remarks>
public sealed record TreeGrower
{
    /// <summary>
    /// The tree it grows by itself.
    /// </summary>
    public string? Tree { get; init; }

    /// <summary>
    /// The tree it grows instead of <see cref="Tree"/> with <see cref="SecondaryChance"/>, like a fancy oak.
    /// </summary>
    public string? SecondaryTree { get; init; }

    /// <summary>
    /// The tree four saplings in a square grow into, like a dark oak.
    /// </summary>
    public string? MegaTree { get; init; }

    /// <summary>
    /// The tree four saplings grow into instead of <see cref="MegaTree"/> with <see cref="SecondaryChance"/>, like a
    /// mega pine.
    /// </summary>
    public string? SecondaryMegaTree { get; init; }

    /// <summary>
    /// The tree it grows near flowers, like an oak with a bee nest.
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

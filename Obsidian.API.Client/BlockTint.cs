namespace Obsidian.API.Client;

/// <summary>
/// Where a block's tint comes from.
/// </summary>
public enum BlockTintSource
{
    /// <summary>
    /// The biome's grass colour, like grass and ferns.
    /// </summary>
    Grass,

    /// <summary>
    /// The biome's foliage colour, like oak leaves and vines.
    /// </summary>
    Foliage,

    /// <summary>
    /// The biome's dry foliage colour, like leaf litter.
    /// </summary>
    DryFoliage,

    /// <summary>
    /// The biome's water colour.
    /// </summary>
    Water,

    /// <summary>
    /// A fixed colour, like birch leaves.
    /// </summary>
    Fixed,

    /// <summary>
    /// Fixed colours selected by each face's tint index.
    /// </summary>
    Indexed
}

/// <summary>
/// The colour a block's faces are tinted with, like vanilla's block colours: faces whose model gives them a
/// <c>tintindex</c> are multiplied by its colour for that index.
/// Register it with <see cref="IClientRegistry.RegisterBlockTint"/>.
/// </summary>
public sealed record BlockTint
{
    private BlockTint(BlockTintSource source, int color)
    {
        this.Source = source;
        this.Color = color;
    }

    /// <summary>
    /// The biome's grass colour, blended across nearby biomes; vanilla's grass block item colour for the block's item.
    /// </summary>
    public static BlockTint Grass { get; } = new(BlockTintSource.Grass, 0);

    /// <summary>
    /// The biome's foliage colour, blended across nearby biomes; vanilla's default foliage colour for the block's item.
    /// </summary>
    public static BlockTint Foliage { get; } = new(BlockTintSource.Foliage, 0);

    /// <summary>
    /// The biome's dry foliage colour, blended across nearby biomes; vanilla's default dry foliage colour for the
    /// block's item.
    /// </summary>
    public static BlockTint DryFoliage { get; } = new(BlockTintSource.DryFoliage, 0);

    /// <summary>
    /// The biome's water colour, blended across nearby biomes; vanilla's default water colour for the block's item.
    /// </summary>
    public static BlockTint Water { get; } = new(BlockTintSource.Water, 0);

    /// <summary>
    /// Where the colour comes from.
    /// </summary>
    public BlockTintSource Source { get; }

    /// <summary>
    /// The colour, as <c>0xRRGGBB</c>, for <see cref="BlockTintSource.Fixed"/>; 0 for other sources.
    /// </summary>
    public int Color { get; }

    /// <summary>
    /// Fixed colours as <c>0xRRGGBB</c>, by tint index, for <see cref="BlockTintSource.Indexed"/>; empty otherwise.
    /// The list is copied when created and cannot be changed. Missing indexes leave faces untinted.
    /// </summary>
    public IReadOnlyList<int> Colors { get; private init; } = Array.Empty<int>();

    /// <summary>
    /// A fixed colour, as <c>0xRRGGBB</c>, for the block and its item.
    /// </summary>
    public static BlockTint Fixed(int color) => new(BlockTintSource.Fixed, color & 0xFFFFFF);

    /// <summary>
    /// Fixed colours for the block and its item: entry zero colours tint index zero, entry one index one, and so on.
    /// Missing indexes and faces without a tint index stay untinted. Colours are copied and masked to 24-bit RGB.
    /// </summary>
    public static BlockTint FixedByIndex(params int[] colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        return new(BlockTintSource.Indexed, 0)
        {
            Colors = Array.AsReadOnly(Array.ConvertAll(colors, color => color & 0xFFFFFF))
        };
    }
}

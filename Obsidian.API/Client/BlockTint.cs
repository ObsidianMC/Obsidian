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
    Fixed
}

/// <summary>
/// The colour a block's faces are tinted with, like vanilla's block colours: faces whose model gives them a
/// <c>tintindex</c> are multiplied by it. Register it with <see cref="IClientRegistry.RegisterBlockTint"/>.
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
    /// The colour, as <c>0xRRGGBB</c>, for <see cref="BlockTintSource.Fixed"/>; 0 for the biome sources.
    /// </summary>
    public int Color { get; }

    /// <summary>
    /// A fixed colour, as <c>0xRRGGBB</c>, for the block and its item.
    /// </summary>
    public static BlockTint Fixed(int color) => new(BlockTintSource.Fixed, color & 0xFFFFFF);
}

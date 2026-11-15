namespace Obsidian.API.Registry.Codecs.PaintingVariant;
/// <summary>
/// Contains a painting's client asset and dimensions in blocks.
/// </summary>
public sealed record class PaintingVariantElement
{
    /// <summary>
    /// Gets or sets the painting asset identifier, serialized as <c>asset_id</c>.
    /// </summary>
    public string AssetId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the painting's height in blocks.
    /// </summary>
    public int Height { get; set; }
    /// <summary>
    /// Gets or sets the painting's width in blocks.
    /// </summary>
    public int Width { get; set; }
}

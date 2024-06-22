namespace Obsidian.API.Registry.Codecs.PaintingVariant;
public sealed record class PaintingVariantElement
{
    public string AssetId { get; set; } = default!;

    public int Height { get; set; }
    public int Width { get; set; }
}

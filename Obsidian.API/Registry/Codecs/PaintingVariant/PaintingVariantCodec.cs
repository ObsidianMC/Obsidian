namespace Obsidian.API.Registry.Codecs.PaintingVariant;
public sealed class PaintingVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public PaintingVariantElement Element { get; internal set; } = new();

    internal PaintingVariantCodec() { }
}

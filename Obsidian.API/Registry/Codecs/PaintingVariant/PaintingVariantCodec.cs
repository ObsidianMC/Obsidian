using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.PaintingVariant;
public sealed class PaintingVariantCodec : ICodec
{
    public required string Name { get; init; }

    public required int Id { get; init; }

    public PaintingVariantElement Element { get; internal set; } = new();

    internal PaintingVariantCodec() { }

    public void WriteElement(INbtWriter writer)
    {
        writer.WriteString("asset_id", this.Element.AssetId);
        writer.WriteInt("height", this.Element.Height);
        writer.WriteInt("width", this.Element.Width);
    }
}

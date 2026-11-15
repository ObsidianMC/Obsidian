using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.PaintingVariant;
/// <summary>
/// Represents a painting variant registry entry and its synchronized element data.
/// </summary>
public sealed class PaintingVariantCodec : ICodec
{
    /// <summary>
    /// Gets the entry's namespaced registry identifier.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the numeric registry ID used on the network.
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// Gets the painting variant data serialized for this entry.
    /// </summary>
    public PaintingVariantElement Element { get; internal set; } = new();

    internal PaintingVariantCodec() { }

    /// <summary>
    /// Writes this entry's painting variant fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer)
    {
        writer.WriteString("asset_id", this.Element.AssetId);
        writer.WriteInt("height", this.Element.Height);
        writer.WriteInt("width", this.Element.Width);
    }
}

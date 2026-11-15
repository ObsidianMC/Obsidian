using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.WolfVariant;
/// <summary>
/// Represents a wolf appearance variant registry entry and its synchronized element data.
/// </summary>
public sealed record class WolfVariantCodec : ICodec
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
    /// Gets the wolf appearance variant data serialized for this entry.
    /// </summary>
    public required WolfVariantElement Element { get; init; }

    /// <summary>
    /// Writes this entry's wolf appearance variant fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.ZombieNautilusVariant;

/// <summary>
/// Represents a zombie nautilus variant registry entry and its synchronized element data.
/// </summary>
public sealed class ZombieNautilusVariantCodec : ICodec
{
    /// <summary>
    /// Gets the entry's namespaced registry identifier.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the numeric registry ID used on the network.
    /// </summary>
    public required int Id {  get; init; }

    /// <summary>
    /// Gets the zombie nautilus variant data serialized for this entry.
    /// </summary>
    public required ZombieNautilusVariantElement Element { get; init; }


    /// <summary>
    /// Writes this entry's zombie nautilus variant fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

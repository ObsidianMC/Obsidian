
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs;
/// <summary>
/// Represents a biome-dependent entity variant registry entry and its synchronized element data.
/// </summary>
public sealed class BiomeVariantCodec : ICodec
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
    /// Gets or sets the biome-dependent entity variant data serialized for this entry.
    /// </summary>
    public required BiomeVariantElement Element { get; set; }

    /// <summary>
    /// Writes this entry's biome-dependent entity variant fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

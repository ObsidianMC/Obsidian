using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dimensions;

/// <summary>
/// Represents a dimension type registry entry and its synchronized element data.
/// </summary>
public sealed record class DimensionCodec : ICodec
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
    /// The entry's data. A plugin's entry gets a new element when its plugin reloads, and keeps its instance and id.
    /// </summary>
    public required DimensionElement Element { get; set; }

    /// <summary>
    /// Writes this entry's dimension type fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;
/// <summary>
/// Represents a client dialog registry entry and its synchronized element data.
/// </summary>
public sealed record class DialogCodec : ICodec
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
    /// Gets the client dialog data serialized for this entry.
    /// </summary>
    public required DialogElement Element { get; init; }

    /// <summary>
    /// Writes this entry's client dialog fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

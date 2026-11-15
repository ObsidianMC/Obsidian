using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Chat;

/// <summary>
/// Represents a chat type registry entry and its synchronized element data.
/// </summary>
public sealed record class ChatTypeCodec : ICodec
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
    /// Gets the chat type data serialized for this entry.
    /// </summary>
    public required ChatElement Element { get; init; }

    /// <summary>
    /// Writes this entry's chat type fields to the registry element NBT payload.
    /// </summary>
    public void WriteElement(INbtWriter writer) => this.Element.Write(writer);
}

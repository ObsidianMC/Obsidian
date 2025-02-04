using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to the client to open a book GUI.
/// </summary>
public partial class OpenBookPacket
{
    /// <summary>
    /// The hand that is holding the book.
    /// </summary>
    [Field(0), ActualType(typeof(int)), VarLength]
    public Hand Hand { get; set; }

    public override void Serialize(INetStreamWriter writer) => writer.WriteVarInt(this.Hand);
}

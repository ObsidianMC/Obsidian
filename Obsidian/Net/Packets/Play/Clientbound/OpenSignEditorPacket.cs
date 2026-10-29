namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to tell the client to open the sign editor screen.
/// </summary>
public partial class OpenSignEditorPacket
{
    /// <summary>
    /// The location of the sign to edit.
    /// </summary>
    public Vector Position { get; init; }

    /// <summary>
    /// The side of the sign the editor opens on.
    /// </summary>
    public SignTextSlot Slot { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePosition(this.Position);
        writer.WriteVarInt((int)this.Slot);
    }
}

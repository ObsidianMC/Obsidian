namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to tell the client to open the sign editor screen.
/// </summary>
public partial class OpenSignEditorPacket
{
    /// <summary>
    /// The location of the sign to edit.
    /// </summary>
    public VectorF Location { get; set; }

    /// <summary>
    /// True if the editor is opened for the front text, false for the back text.
    /// </summary>
    public bool IsFrontText { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WritePositionF(Location);
        writer.WriteBoolean(IsFrontText);
    }
}

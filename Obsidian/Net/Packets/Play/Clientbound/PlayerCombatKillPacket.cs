namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to tell the client to show the respawn screen.
/// </summary>
public partial class PlayerCombatKillPacket
{
    /// <summary>
    /// Entity ID of the dead player. Should match the ID on the client.
    /// </summary>
    public int PlayerID { get; set; }

    /// <summary>
    /// The death message to display.
    /// </summary>
    public ChatMessage Message { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(PlayerID);
        writer.WriteChat(Message);
    }
}

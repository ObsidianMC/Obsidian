using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent when a player exits the combat state. This packet is unused by the vanilla Minecraft client.
/// </summary>
public partial class PlayerCombatEndPacket
{
    /// <summary>
    /// The duration of the combat state in ticks.
    /// </summary>
    [Field(0), VarLength]
    public int Duration { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Duration);
    }
}

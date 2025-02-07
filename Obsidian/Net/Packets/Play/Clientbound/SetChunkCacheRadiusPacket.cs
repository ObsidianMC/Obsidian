using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Sent to change the view distance of the client.
/// </summary>
public partial class SetChunkCacheRadiusPacket
{
    /// <summary>
    /// The new view distance.
    /// </summary>
    [Field(0), VarLength]
    public int ViewDistance { get; set; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(ViewDistance);
    }
}

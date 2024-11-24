namespace Obsidian.Net.Packets.Status.Clientbound;
public partial class PongResponsePacket
{
    public required long Timestamp { get; set; }

    public override void Serialize(INetStreamWriter writer) => writer.WriteLong(Timestamp);
}

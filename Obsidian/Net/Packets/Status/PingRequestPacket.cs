
namespace Obsidian.Net.Packets.Status.Serverbound;
public partial class PingRequestPacket
{
    public long Timestamp { get; private set; }
    public override void Populate(INetStreamReader reader)
    {
        this.Timestamp = reader.ReadLong();
    }
}

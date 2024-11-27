using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class KeepAlivePacket
{
    [Field(0)]
    public long KeepAliveId { get; set; }

    public KeepAlivePacket() { }

    public KeepAlivePacket(long id)
    {
        KeepAliveId = id;
    }

    public override void Populate(INetStreamReader stream)
    {
        this.KeepAliveId = stream.ReadLong();
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteLong(KeepAliveId);
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        await player.client.HandleKeepAliveAsync(this);
    }
}

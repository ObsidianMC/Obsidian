using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Common;
public partial record class KeepAlivePacket
{
    [Field(0)]
    public long KeepAliveId { get; private set; }

    public int Id { get; init; }

    public KeepAlivePacket()
    {

    }

    public KeepAlivePacket(long id)
    {
        KeepAliveId = id;
    }

    public async ValueTask HandleAsync(Server server, Player player)
    {
        await player.client.HandleKeepAliveAsync(this);
    }
}

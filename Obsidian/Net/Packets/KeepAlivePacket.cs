using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class KeepAlivePacket : IClientboundPacket, IServerboundPacket
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

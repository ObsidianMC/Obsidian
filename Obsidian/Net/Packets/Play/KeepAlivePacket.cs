using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class KeepAlivePacket : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public long KeepAliveId { get; private set; }

    public int Id => 0x20;

    public KeepAlivePacket()
    {
    }

    public KeepAlivePacket(long id)
    {
        KeepAliveId = id;
    }

    public ValueTask HandleAsync(Server server, Player player)
    {
        player.client.HandleKeepAlive(this);
        return ValueTask.CompletedTask;
    }
}

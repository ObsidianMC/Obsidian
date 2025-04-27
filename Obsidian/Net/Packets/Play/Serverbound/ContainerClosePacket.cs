using Obsidian.API.Events;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class ContainerClosePacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    public async override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        if (ContainerId == 0)
            return;

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClosedEventArgs(player, server) { Container = player.OpenedContainer! });
    }

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
    }
}

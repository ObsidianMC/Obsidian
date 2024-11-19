using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class ContainerClosePacket
{
    [Field(0)]
    public byte WindowId { get; private set; }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        if (WindowId == 0)
            return;

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClosedEventArgs(player, server) { Container = player.OpenedContainer! });
    }
}

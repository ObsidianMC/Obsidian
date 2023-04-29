using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play;

public partial class CloseContainerPacket : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public byte WindowId { get; private set; }

    public int Id => 0x11;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (WindowId == 0)
            return;

        await server.Events.InvokeContainerClosedAsync(new ContainerClosedEventArgs(player) { Container = player.OpenedContainer });

        player.OpenedContainer = null;
    }
}

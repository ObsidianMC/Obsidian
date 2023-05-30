using Obsidian.API.Builders;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play;

public partial class CloseContainerPacket : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public byte WindowId { get; private set; }

    public int Id => 0x11;

    public async ValueTask HandleAsync(Server server, Player player)
    {
        if (WindowId == 0 || (player.OpenedContainer is not IBlockEntity tileEntity))
            return;

        await server.Events.ContainerClosed.InvokeAsync(new ContainerClosedEventArgs(player) { Container = player.OpenedContainer! });
    }
}

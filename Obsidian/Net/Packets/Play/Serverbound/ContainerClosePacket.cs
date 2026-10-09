using Obsidian.API.Events;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ContainerClosePacket
{
    [Field(0)]
    public int ContainerId { get; private set; }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player.OpenedContainer is null && this.ContainerId == 0)
        {
            await Obsidian.Events.MainEventHandler.ReturnCraftingItemsAsync(player, player.Inventory, 2);
            return;
        }

        if (player.OpenedContainer is null || this.ContainerId != player.CurrentContainerId)
            return;

        if (player.OpenedContainer is Obsidian.Entities.MerchantContainer merchant)
        { await merchant.CloseAsync(player); return; }

        await server.EventDispatcher.ExecuteEventAsync(new ContainerClosedEventArgs(player, server, player.OpenedContainer!));
    }

    public override void Populate(INetStreamReader reader)
    {
        this.ContainerId = reader.ReadVarInt();
    }
}

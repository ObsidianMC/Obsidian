using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // Vanilla's MinecartItem.useOn: a minecart item used on a rail puts the cart on it; on anything else it does
    // nothing.
    private async ValueTask<bool> TryPlaceMinecartAsync(IClientPlayer player, int handSlot, ItemStack? item, IBlock block)
    {
        if (item is not { Count: > 0 } || VehicleItems.MinecartFor(item.Holder) is not EntityType type)
            return false;

        player.Client.SendPacket(new BlockChangedAckPacket { SequenceID = this.Sequence });
        if (player.GameMode is GameMode.Spectator or GameMode.Adventure
            || player.Health <= 0
            || player.Level is not AbstractLevel level
            || VehicleItems.PlaceMinecart(level, type, this.Position, block, item) is null)
        {
            return true;
        }

        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(handSlot, 1);
            await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
            {
                ContainerId = 0,
                Slot = (short)handSlot,
                SlotData = player.Inventory.GetItem(handSlot)
            });
        }

        player.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");
        return true;
    }
}

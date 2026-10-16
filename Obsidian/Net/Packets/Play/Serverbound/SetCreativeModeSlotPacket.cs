using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class SetCreativeModeSlotPacket
{
    [Field(0)]
    public short ClickedSlot { get; private set; }

    [Field(1)]
    public ItemStack? ClickedItem { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.ClickedSlot = reader.ReadShort();
        // Vanilla's creative slot packet sends the stack with its components length-prefixed (the untrusted codec).
        this.ClickedItem = reader.ReadUntrustedItemStack();
    }

    /// <summary>
    /// Sets a slot of the player's inventory from the creative inventory screen, like vanilla: only in creative mode,
    /// in the inventory's slots 1 to 45 (as the player's inventory window numbers them, whatever window is open),
    /// with a stack no larger than its item's limit. A negative slot drops the stack instead, as long as the player
    /// isn't dropping too fast.
    /// </summary>
    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (player.GameMode != GameMode.Creative)
            return;

        var item = this.ClickedItem.IsNullOrAir() ? null : this.ClickedItem;
        var fits = item is null || item.Count >= 1 && item.Count <= item.MaxStackSize;
        var inventory = player.Inventory;

        if (this.ClickedSlot >= 1 && this.ClickedSlot <= 45 && this.ClickedSlot < inventory.Size && fits)
        {
            using (await inventory.LockAsync())
                inventory.SetItem(this.ClickedSlot, item);

            if (player.CurrentHeldItemSlot == this.ClickedSlot)
                ShowHeldItem(player);

            return;
        }

        if (this.ClickedSlot < 0
            && fits
            && item is not null
            && player is Player dropper
            && dropper.CountCreativeDrop())
        {
            ItemEntity.Drop(player, item);
        }
    }

    private static void ShowHeldItem(IClientPlayer player)
    {
        player.Level.PacketBroadcaster.QueuePacketToLevel(player.Level, new SetEquipmentPacket
        {
            EntityId = player.EntityId,
            Equipment =
            [
                new()
                {
                    Item = player.GetHeldItem(),
                    Slot = EquipmentSlot.MainHand
                }
            ]
        }, player.EntityId);
    }
}

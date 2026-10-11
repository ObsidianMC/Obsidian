using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Mob
{
    protected async ValueTask<bool> TryCaptureInBucketAsync(IPlayer player, InteractionHand hand, Material bucketType)
    {
        if (!Alive || player.Health <= 0 || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return false;
        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (item is not { Count: > 0, Type: Material.WaterBucket }) return false;
        var data = EntityNbt.Save(this)!;
        foreach (var name in new[] { "id", "UUID", "Pos", "Motion", "Rotation" }) data.Remove(name);
        data.Set(new NbtTag<bool>("FromBucket", true));
        var bucket = ItemsRegistry.GetSingleItem(bucketType);
        bucket[DataComponentType.BucketEntityData] = new BucketEntityDataComponent { Value = data };
        if (player.GameMode != GameMode.Creative && item.Count == 1)
            player.Inventory.SetItem(slot, bucket);
        else
        {
            if (player.GameMode != GameMode.Creative) player.Inventory.RemoveItem(slot, 1);
            var added = player.Inventory.AddItem(bucket);
            if (added < 0) DropItem(bucket);
            else await player.SendInventorySlotAsync(added);
        }
        await player.SendInventorySlotAsync(slot);
        await RemoveAsync();
        return true;
    }
}

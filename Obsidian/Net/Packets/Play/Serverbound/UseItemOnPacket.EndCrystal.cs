using Obsidian.Entities;
using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    private async ValueTask<bool> TryPlaceEndCrystalAsync(IPlayer player, int handSlot, ItemStack? item)
    {
        if (item is not { Count: > 0, Type: Material.EndCrystal }) return false;
        player.RequireClient("player operation").SendPacket(new BlockChangedAckPacket { SequenceID = Sequence });
        if (player.GameMode == GameMode.Spectator || player.Health <= 0 || player.Level is not AbstractLevel level ||
            (player.Position - (VectorD)Position).MagnitudeSquared() > 36) return true;
        var terrain = new MobTerrain(level);
        if (terrain.GetBlock(Position)?.Material is not Material.Obsidian and not Material.Bedrock) return true;
        var above = Position + Vector.Up;
        if (level.IsOutsideBuildHeight(above.Y) || terrain.GetBlock(above)?.IsAir != true) return true;
        var bounds = new BoundingBox((VectorD)above, (VectorD)above + new VectorD(1, 2, 1));
        if (level.GetEntitiesInRange((VectorD)above, 4).Any(entity =>
            entity.Dimension.CreateBBFromPosition(entity.Position).Intersects(bounds))) return true;
        var crystal = new EndCrystal { Level = level, EntityId = Server.GetNextEntityId(),
            Position = (VectorD)above + new VectorD(0.5, 0, 0.5), ShowBottom = false };
        level.SpawnEntity(crystal);
        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(handSlot, 1);
            await player.RequireClient("player operation").QueuePacketAsync(new ContainerSetSlotPacket
            { ContainerId = 0, Slot = (short)handSlot, SlotData = player.Inventory.GetItem(handSlot) });
        }
        return true;
    }
}

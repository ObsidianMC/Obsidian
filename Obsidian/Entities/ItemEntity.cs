using Obsidian.API.Inventory;

using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private static readonly TimeSpan DropWaitTime = TimeSpan.FromSeconds(.5);
    private int age;

    public ItemStack Item { get; set; }

    public bool CanPickup { get; set; }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => this.Type = EntityType.Item;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        if (velocity is { } initial)
            Motion = new VectorF((float)initial.X, (float)initial.Y, (float)initial.Z);
        base.SpawnEntity(velocity, additionalData);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();
        if (++age >= 6000)
        {
            await RemoveAsync();
            return;
        }
        var terrain = new MobTerrain(Level);
        var next = EntityMovement.Move(this, terrain, VectorF.Zero, 0.04f, 0.98f);
        if (Level is AbstractLevel level && !level.TryMoveEntity(this, Position, next))
            return;
        if (next != Position)
            await UpdateAsync(next, MovementFlags);

        if (!CanPickup && DateTimeOffset.UtcNow - this.TimeDropped > DropWaitTime)
            this.CanPickup = true;

        foreach (var ent in this.Level.GetNonPlayerEntitiesInRange(this.Position, 0.5f))
        {
            if (ent is not ItemEntity itemEntity)
                continue;

            if (itemEntity == this || !Item.Equals(itemEntity.Item))
                continue;

            var transferred = Math.Min(Item.MaxStackSize - Item.Count, itemEntity.Item.Count);
            if (transferred <= 0)
                continue;
            Item.Count += transferred;
            itemEntity.Item.Count -= transferred;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this }, EntityId);
            if (itemEntity.Item.Count == 0)
                await itemEntity.RemoveAsync();
            else
                PacketBroadcaster.QueuePacketToLevelInRange(Level, itemEntity.Position,
                    new SetEntityDataPacket { EntityId = itemEntity.EntityId, Entity = itemEntity }, itemEntity.EntityId);
        }
    }
}

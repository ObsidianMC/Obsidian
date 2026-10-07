using Obsidian.API.Inventory;
using Obsidian.Nbt;

using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private static readonly TimeSpan DropWaitTime = TimeSpan.FromSeconds(.5);
    private int age;
    internal void SetExtendedLifetime() => age = -6000;

    public ItemStack Item { get; set; }

    public bool CanPickup { get; set; }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => this.Type = EntityType.Item;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        if (velocity is { } initial)
            Motion = new VectorD(initial.X, initial.Y, initial.Z);
        base.SpawnEntity(velocity, additionalData);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    // Vanilla's default pickup delay is 10 ticks; a stack that can't be picked up yet keeps it.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.Item.ToNbt("Item"));
        tag.Set(new NbtTag<short>("Age", (short)Math.Clamp(age, short.MinValue, short.MaxValue)));
        tag.Set(new NbtTag<short>("PickupDelay", (short)(this.CanPickup ? 0 : 10)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = stack;

        if (tag.TryGetTag<NbtTag<short>>("Age", out var savedAge)) age = savedAge.Value;
        this.CanPickup = tag.TryGetTag<NbtTag<short>>("PickupDelay", out var delay) && delay.Value == 0;
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
        var next = EntityMovement.Move(this, terrain, VectorD.Zero, 0.04f, 0.98f);
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

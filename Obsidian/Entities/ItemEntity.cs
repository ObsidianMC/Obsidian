using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private int age;
    internal void SetExtendedLifetime() => age = -6000;
    // Region ticks and player movement packets can transfer the same stack concurrently.
    internal static readonly object TransferLock = new();
    internal bool Removed { get; set; }
    private int pickupDelay = 10;

    public ItemStack Item { get; set; }

    public bool CanPickup
    {
        get => this.pickupDelay == 0;
        set => this.pickupDelay = value ? 0 : 10;
    }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    public ItemEntity() => this.Type = EntityType.Item;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        if (velocity is { } initial)
            Motion = new VectorD(initial.X, initial.Y, initial.Z);
        base.SpawnEntity(velocity, additionalData);
    }

    internal static bool Drop(IPlayer player, ItemStack stack)
    {
        var direction = player.GetLookDirection();
        var motion = new VectorD(direction.X * 0.3, direction.Y * 0.3 + 0.1, direction.Z * 0.3);
        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = new ItemStack(stack, stack.Count),
            Level = player.Level,
            Position = new VectorD(player.Position.X, player.HeadY - 0.3, player.Position.Z),
            Motion = motion
        };
        if (!player.Level.TryAddEntity(item))
            return false;
        item.SpawnEntity(new Velocity(motion.X, motion.Y, motion.Z));
        return true;
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
        tag.Set(new NbtTag<short>("PickupDelay", (short)this.pickupDelay));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = stack;

        if (tag.TryGetTag<NbtTag<short>>("Age", out var savedAge)) age = savedAge.Value;
        this.pickupDelay = tag.TryGetTag<NbtTag<short>>("PickupDelay", out var delay) ? Math.Max(0, (int)delay.Value) : 10;
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();
        lock (TransferLock)
        {
            if (this.Removed)
                return;
            if (this.pickupDelay > 0)
                this.pickupDelay--;
        }
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

        foreach (var ent in this.Level.GetNonPlayerEntitiesInRange(this.Position, 0.5f))
        {
            if (ent is not ItemEntity itemEntity)
                continue;

            if (itemEntity.EntityId <= this.EntityId)
                continue;

            bool remove;
            lock (TransferLock)
            {
                if (this.Removed || itemEntity.Removed || this.Item.IsNullOrAir() || itemEntity.Item.IsNullOrAir() || this.Item != itemEntity.Item)
                    continue;
                var count = Math.Min(this.Item.MaxStackSize - this.Item.Count, itemEntity.Item.Count);
                if (count <= 0)
                    continue;
                this.Item.Count += count;
                itemEntity.Item.Count -= count;
                this.pickupDelay = Math.Max(this.pickupDelay, itemEntity.pickupDelay);
                this.age = Math.Min(this.age, itemEntity.age);
                remove = itemEntity.Removed = itemEntity.Item.Count == 0;
                this.SendItemUpdate();
                if (!remove)
                    itemEntity.SendItemUpdate();
            }
            if (remove)
                await itemEntity.RemoveAsync();
        }
    }

    internal void SendItemUpdate() => this.PacketBroadcaster.QueuePacketToLevel(this.Level,
        new SetEntityDataPacket { EntityId = this.EntityId, Entity = this });

    public override async ValueTask RemoveAsync()
    {
        lock (TransferLock)
            this.Removed = true;
        await base.RemoveAsync();
    }

}

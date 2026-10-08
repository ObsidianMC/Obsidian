using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
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
        tag.Set(new NbtTag<short>("PickupDelay", (short)this.pickupDelay));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = stack;

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

        await this.MoveItemAsync();

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

    private async ValueTask MoveItemAsync()
    {
        if (this.Level is not AbstractLevel level)
            return;

        var motion = this.Motion;
        if (!this.NoGravity)
            motion.Y -= 0.04;
        var position = this.Position;
        var box = new BoundingBox(position - new VectorD(0.125, 0, 0.125), position + new VectorD(0.125, 0.25, 0.125));
        var obstacles = new List<BoundingBox>();
        for (var x = (int)Math.Floor(box.Min.X + Math.Min(0, motion.X)); x <= Math.Floor(box.Max.X + Math.Max(0, motion.X)); x++)
            for (var y = (int)Math.Floor(box.Min.Y + Math.Min(0, motion.Y)); y <= Math.Floor(box.Max.Y + Math.Max(0, motion.Y)); y++)
                for (var z = (int)Math.Floor(box.Min.Z + Math.Min(0, motion.Z)); z <= Math.Floor(box.Max.Z + Math.Max(0, motion.Z)); z++)
                {
                    var block = await this.Level.GetBlockAsync(new Vector(x, y, z));
                    if (block is null)
                        return;
                    var offset = new VectorD(x, y, z);
                    foreach (var shape in PortalCollision.GetShapes(block))
                        obstacles.Add(new BoundingBox(shape.Min + offset, shape.Max + offset));
                }

        var dy = Clip(box, obstacles, motion.Y, 1);
        box = new BoundingBox(box.Min + new VectorD(0, dy, 0), box.Max + new VectorD(0, dy, 0));
        var dx = Clip(box, obstacles, motion.X, 0);
        box = new BoundingBox(box.Min + new VectorD(dx, 0, 0), box.Max + new VectorD(dx, 0, 0));
        var dz = Clip(box, obstacles, motion.Z, 2);
        var next = position + new VectorD(dx, dy, dz);
        var grounded = motion.Y < 0 && dy != motion.Y;
        var previousRegion = level.GetRegionForLocation(position) as Region;
        var nextRegion = level.GetRegionForLocation(next) as Region;
        if (nextRegion is null)
            return;
        lock (TransferLock)
        {
            if (this.Removed)
                return;
            this.Position = next;
            if (previousRegion != nextRegion)
            {
                previousRegion?.Entities.TryRemove(this.EntityId, out _);
                nextRegion.Entities.TryAdd(this.EntityId, this);
            }
            var friction = grounded ? 0.588 : 0.98;
            this.Motion = new VectorD(dx == motion.X ? motion.X * friction : 0,
                dy == motion.Y ? motion.Y * 0.98 : -motion.Y * 0.5, dz == motion.Z ? motion.Z * friction : 0);
            if (grounded && Math.Abs(this.Motion.Y) < 0.025)
                this.Motion = new VectorD(this.Motion.X, 0, this.Motion.Z);
            if (next != position)
                this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, next, new EntityPositionSyncPacket
                {
                    IdValue = this.EntityId,
                    Values = new PositionMoveRotation { Position = next, DeltaMovement = this.Motion, YRot = this.Yaw, XRot = this.Pitch },
                    OnGround = grounded
                });
        }
    }

    private static double Clip(BoundingBox box, List<BoundingBox> obstacles, double movement, int axis)
    {
        static double Coordinate(VectorD value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
        foreach (var obstacle in obstacles)
        {
            var first = (axis + 1) % 3;
            var second = (axis + 2) % 3;
            if (Coordinate(box.Max, first) <= Coordinate(obstacle.Min, first) || Coordinate(box.Min, first) >= Coordinate(obstacle.Max, first) ||
                Coordinate(box.Max, second) <= Coordinate(obstacle.Min, second) || Coordinate(box.Min, second) >= Coordinate(obstacle.Max, second))
                continue;
            if (movement > 0 && Coordinate(box.Max, axis) <= Coordinate(obstacle.Min, axis))
                movement = Math.Min(movement, Coordinate(obstacle.Min, axis) - Coordinate(box.Max, axis));
            else if (movement < 0 && Coordinate(box.Min, axis) >= Coordinate(obstacle.Max, axis))
                movement = Math.Max(movement, Coordinate(obstacle.Max, axis) - Coordinate(box.Min, axis));
        }
        return movement;
    }
}

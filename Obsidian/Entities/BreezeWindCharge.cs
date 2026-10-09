using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

internal sealed class BreezeWindCharge : Entity
{
    private IEntity? owner;
    private Guid ownerUuid;
    private int age;
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal BreezeWindCharge(ILevel level)
    {
        Level = level; Type = EntityType.BreezeWindCharge; NoGravity = true;
        Dimension = new EntityDimension { Width = 0.3125f, Height = 0.3125f };
    }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal BreezeWindCharge(Breeze breeze, VectorD position, VectorD direction) : this(breeze.Level)
    {
        owner = breeze; ownerUuid = breeze.Uuid; EntityId = Server.GetNextEntityId(); Position = position;
        Motion = direction.Magnitude > 0.001f ? direction / direction.Magnitude * 0.7f : VectorD.Zero;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }
    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), owner?.EntityId ?? 0);
    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position)) return;
        if (++age >= 200 || Position.Y < -128) { await RemoveAsync(); return; }
        if (owner == null && ownerUuid != Guid.Empty && (age == 1 || age % 20 == 0))
            owner = Level.GetEntitiesInRange(Position, float.MaxValue).FirstOrDefault(entity => entity.Uuid == ownerUuid);
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var swept = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.15f), VectorD.Max(Position, end) + new VectorD(0.15f));
        var fraction = 1d;
        IEntity? target = null;
        foreach (var shape in terrain.GetCollisions(swept))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit) fraction = Math.Min(fraction, hit);
        foreach (var entity in Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 3))
        {
            if (entity is not Living || entity.Health <= 0 || ReferenceEquals(entity, owner) || entity is Breeze ||
                entity is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }) continue;
            var bounds = entity.Dimension.CreateBBFromPosition(entity.Position);
            if (MobTerrain.RayIntersection(new BoundingBox(bounds.Min - new VectorD(0.15f), bounds.Max + new VectorD(0.15f)), Position, Motion) is double hit && hit < fraction)
            { fraction = hit; target = entity; }
        }
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next)) return;
        Position = next; BoundingBox = Dimension.CreateBBFromPosition(next);
        if (fraction < 1 || target != null)
        {
            if (target != null) await target.DamageAsync(owner ?? this, 1);
            await BurstAsync(terrain);
            await RemoveAsync(); return;
        }
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
    }
    private async ValueTask BurstAsync(MobTerrain terrain)
    {
        foreach (var entity in Level.GetEntitiesInRange(Position, 3).OfType<Living>())
        {
            if (!entity.Alive || entity is IPlayer { GameMode: GameMode.Spectator } || entity is Creaking { CanMove: false }) continue;
            var delta = entity.Position + new VectorD(0, entity.Dimension.Height * 0.5f, 0) - Position;
            var distance = delta.Magnitude;
            if (distance >= 3 || !terrain.HasLineOfSight(Position, entity.Position + new VectorD(0, 0.1f, 0))) continue;
            if (distance < 0.001f) delta = new VectorD(0, 1, 0); else delta /= distance;
            var resistance = Math.Clamp(entity.GetAttributeValue("minecraft:generic.knockback_resistance"), 0, 1);
            entity.Motion += delta * ((1 - distance / 3) * 1.2f * (1 - resistance));
        }
        if (Level.LevelData.GetBooleanRule("minecraft:mob_griefing", true))
        {
            var center = (Vector)Position.Floor();
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
            for (var z = -2; z <= 2; z++)
            {
                var point = center + new Vector(x, y, z);
                if (((VectorD)point - Position).MagnitudeSquared() > 9) continue;
                var block = terrain.GetBlock(point);
                if (block == null) continue;
                if (block.Material is Material.Fire or Material.SoulFire)
                { await Level.SetBlockAsync(point, BlocksRegistry.Get(Material.Air), true); continue; }
                var open = block.GetProperty("open");
                if (open != null && block.Material is not Material.IronDoor and not Material.IronTrapdoor)
                    await Level.SetBlockAsync(point, block.WithProperty("open", open != "true"), true);
                else if (block.Material == Material.Lever)
                    await Level.SetBlockAsync(point, block.WithProperty("powered", block.GetProperty("powered") != "true"), true);
                else if (block.GetProperty("lit") == "true" && (block.Material is Material.Campfire or Material.SoulCampfire ||
                    TagsRegistry.Block.Candles.Entries.Contains(block.RegistryId)))
                    await Level.SetBlockAsync(point, block.WithProperty("lit", false), true);
            }
        }
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag); tag.Set(new NbtTag<int>("ObsidianLife", age));
        if (ownerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(ownerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        age = tag.TryGetTagValue<int>("ObsidianLife", out var ticks) ? Math.Clamp(ticks, 0, 200) : 0;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var uuid) && uuid.Count == 4) ownerUuid = EntityNbt.UuidFromInts(uuid.GetArray());
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }
}

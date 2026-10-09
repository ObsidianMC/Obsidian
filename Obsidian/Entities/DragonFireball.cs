using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

internal sealed class DragonFireball : Entity
{
    internal Guid OwnerUuid { get; private set; }
    private VectorD acceleration;
    private int age;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal DragonFireball(ILevel level)
    {
        Level = level;
        Type = EntityType.DragonFireball;
        Dimension = new EntityDimension { Width = 1, Height = 1 };
        NoGravity = true;
    }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal DragonFireball(EnderDragon owner, VectorD position, VectorD direction) : this(owner.Level)
    {
        EntityId = Server.GetNextEntityId();
        OwnerUuid = owner.Uuid;
        Position = position;
        if (direction.Magnitude > 0.001f) direction /= direction.Magnitude;
        acceleration = direction * 0.1f;
        Motion = direction * 0.2f;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }
    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position)) return;
        if (++age > 600 || Position.Y < -128) { await RemoveAsync(); return; }
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var swept = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.5f), VectorD.Max(Position, end) + new VectorD(0.5f));
        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(swept))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is { } hit) fraction = Math.Min(fraction, hit);
        foreach (var target in Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 3).OfType<Living>())
        {
            if (target.Uuid == OwnerUuid || !target.Alive || target is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }) continue;
            if (MobTerrain.RayIntersection(target.Dimension.CreateBBFromPosition(target.Position), Position, Motion) is { } hit)
                fraction = Math.Min(fraction, hit);
        }
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next)) return;
        Position = next;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (fraction < 1)
        {
            var nearby = Level.GetEntitiesInRange(Position, 4).OfType<Living>().FirstOrDefault(target =>
                target.Uuid != OwnerUuid && target.Alive && (target.Position - Position).MagnitudeSquared() < 16);
            Level.SpawnEntity(new DragonBreathCloud
            {
                Level = Level, EntityId = Server.GetNextEntityId(), OwnerUuid = OwnerUuid,
                Position = nearby?.Position ?? Position, Radius = 3, RadiusPerTick = 4f / 600, Duration = 600
            });
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelEventPacket(2006, (Vector)Position.Floor(), 1));
            await RemoveAsync();
            return;
        }
        Motion = (Motion + acceleration) * 0.95f;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(EntityNbt.DoubleList("ObsidianAcceleration", acceleration));
        tag.Set(new NbtTag<int>("ObsidianLife", age));
        if (OwnerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(OwnerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (!EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out acceleration) && Motion.Magnitude > 0.001f)
            acceleration = Motion / Motion.Magnitude * 0.1f;
        age = Math.Clamp(tag.TryGetTagValue<int>("ObsidianLife", out var ticks) ? ticks : 0, 0, 600);
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4) OwnerUuid = EntityNbt.UuidFromInts(owner.GetArray());
    }
}

internal sealed class DragonBreathCloud : Entity
{
    internal Guid OwnerUuid { get; set; }
    internal float Radius { get; set; } = 3;
    internal float RadiusPerTick { get; set; }
    internal int Duration { get; set; } = 600;
    internal int WaitTime { get; set; } = 20;
    private int age;
    private readonly Dictionary<int, int> nextHit = [];

    internal DragonBreathCloud()
    {
        Type = EntityType.AreaEffectCloud;
        NoGravity = true;
        Dimension = new EntityDimension { Width = 6, Height = 0.5f };
    }
    public override ValueTask DamageAsync(IEntity source, float amount = 1) => default;
    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position)) return;
        if (++age >= WaitTime + Duration) { await RemoveAsync(); return; }
        if (age < WaitTime) return;
        Radius += RadiusPerTick;
        if (Radius < 0.5f) { await RemoveAsync(); return; }
        Dimension = new EntityDimension { Width = Radius * 2, Height = 0.5f };
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (age % 5 != 0) return;
        var owner = Level.GetEntitiesInRange(Position, 256).FirstOrDefault(entity => entity.Uuid == OwnerUuid);
        foreach (var target in Level.GetEntitiesInRange(Position, Radius + 1).OfType<Living>().ToArray())
        {
            if (target.Uuid == OwnerUuid || target is EnderDragon || !target.Alive || target is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator } ||
                !BoundingBox.Intersects(target.Dimension.CreateBBFromPosition(target.Position)) ||
                nextHit.TryGetValue(target.EntityId, out var ready) && age < ready) continue;
            var difference = target.Position - Position;
            if (difference.X * difference.X + difference.Z * difference.Z > Radius * Radius) continue;
            nextHit[target.EntityId] = age + 20;
            if (target.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.ZombifiedPiglin or
                EntityType.Zoglin or EntityType.Skeleton or EntityType.Stray or EntityType.Bogged or EntityType.Parched or
                EntityType.WitherSkeleton or EntityType.Wither or EntityType.Phantom or EntityType.SkeletonHorse or EntityType.ZombieHorse)
            {
                target.Health = Math.Min(target.GetAttributeValue("minecraft:generic.max_health"), target.Health + 4);
                continue;
            }
            if (target is Mob mob) await mob.DamageMagicAsync(owner ?? this, target is Witch ? 0.9f : 6);
            else await target.DamageAsync(owner ?? this, 6);
        }
        foreach (var id in nextHit.Where(entry => age >= entry.Value).Select(entry => entry.Key).ToArray()) nextHit.Remove(id);
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.Float);
        writer.WriteSingle(Radius);
        writer.WriteEntityMetadataType(9, EntityMetadataType.Boolean);
        writer.WriteBoolean(age < WaitTime);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Particle);
        writer.WriteVarInt((int)ParticleType.DragonBreath);
        writer.WriteSingle(1);
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<bool>("ObsidianDragonBreath", true));
        tag.Set(new NbtTag<float>("Radius", Radius));
        tag.Set(new NbtTag<float>("RadiusPerTick", RadiusPerTick));
        tag.Set(new NbtTag<int>("Duration", Duration));
        tag.Set(new NbtTag<int>("WaitTime", WaitTime));
        tag.Set(new NbtTag<int>("Age", age));
        if (OwnerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(OwnerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (tag.TryGetTagValue<float>("Radius", out var radius) && float.IsFinite(radius)) Radius = Math.Clamp(radius, 0, 32);
        if (tag.TryGetTagValue<float>("RadiusPerTick", out var rate) && float.IsFinite(rate)) RadiusPerTick = Math.Clamp(rate, -1, 1);
        Duration = Math.Clamp(tag.TryGetTagValue<int>("Duration", out var duration) ? duration : 600, 0, 72000);
        WaitTime = Math.Clamp(tag.TryGetTagValue<int>("WaitTime", out var wait) ? wait : 20, 0, 72000);
        age = Math.Clamp(tag.TryGetTagValue<int>("Age", out var ticks) ? ticks : 0, 0, 144000);
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4) OwnerUuid = EntityNbt.UuidFromInts(owner.GetArray());
    }
}

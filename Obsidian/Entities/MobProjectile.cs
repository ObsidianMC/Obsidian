using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.Nbt;

namespace Obsidian.Entities;

internal sealed class MobProjectile : Entity
{
    internal IEntity? Owner { get; private set; }
    private Guid ownerUuid;
    private VectorF acceleration;
    private int age;
    private bool resolveOwner = true;
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(ILevel level, EntityType type)
    {
        Level = level;
        Type = type;
        Dimension = new EntityDimension { Width = type == EntityType.Fireball ? 1 : 0.3125f, Height = type == EntityType.Fireball ? 1 : 0.3125f };
        NoGravity = type != EntityType.Snowball;
    }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(Mob owner, EntityType type, VectorF position, VectorF direction) : this(owner.Level, type)
    {
        Owner = owner;
        ownerUuid = owner.Uuid;
        EntityId = Server.GetNextEntityId();
        Position = position;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (direction.Magnitude > 0.0001f)
            direction /= direction.Magnitude;
        Motion = direction * (type == EntityType.Snowball ? 1.6f : 0.2f);
        acceleration = type == EntityType.Snowball ? VectorF.Zero : direction * 0.1f;
        NoGravity = type != EntityType.Snowball;
    }
    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0) =>
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), Owner?.EntityId ?? 0);
    public override ValueTask DamageAsync(IEntity source, float amount = 1)
    {
        if (Type == EntityType.Fireball && source.Level == Level && source.Health > 0 && amount > 0)
        {
            Deflect(source, source.GetLookDirection());
        }
        return default;
    }
    internal void Deflect(IEntity source, VectorF direction)
    {
        if (Type != EntityType.Fireball || source.Level != Level || direction.Magnitude < 0.001f)
            return;
        Owner = source;
        ownerUuid = source.Uuid;
        direction /= direction.Magnitude;
        Motion = direction;
        acceleration = direction * 0.1f;
    }
    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position))
            return;
        if (++age > 600 || Position.Y < -128)
        {
            await RemoveAsync();
            return;
        }
        if (Owner == null && ownerUuid != Guid.Empty && (resolveOwner || age % 20 == 0))
            Owner = Level.GetEntitiesInRange(Position, float.MaxValue).FirstOrDefault(entity => entity.Uuid == ownerUuid);
        resolveOwner = false;
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var swept = new BoundingBox(VectorF.Min(Position, end) - new VectorF(0.3f), VectorF.Max(Position, end) + new VectorF(0.3f));
        var fraction = 1f;
        IEntity? target = null;
        foreach (var shape in terrain.GetCollisions(swept))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is float hit)
                fraction = Math.Min(fraction, hit);
        foreach (var candidate in Level.GetEntitiesInRange(Position, Motion.Magnitude + 3))
        {
            if (candidate is not Living || candidate.Health <= 0 || ReferenceEquals(candidate, Owner) && age < 25 ||
                candidate is IPlayer player && player.Gamemode is Gamemode.Creative or Gamemode.Spectator)
                continue;
            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            if (MobTerrain.RayIntersection(new BoundingBox(bounds.Min - new VectorF(0.3f), bounds.Max + new VectorF(0.3f)), Position, Motion) is float hit && hit < fraction)
            {
                if (candidate is Enderman enderman)
                {
                    enderman.TryAvoidProjectile();
                    continue;
                }
                fraction = hit;
                target = candidate;
            }
        }
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        Position = next;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (fraction < 1 || target != null)
        {
            if (target != null)
            {
                var damage = Type switch
                {
                    EntityType.SmallFireball => target is Living { IsFireImmune: true } ? 0 : 5,
                    EntityType.Snowball => target is Blaze ? 3 : 0,
                    _ => target is Ghast && Owner is IPlayer ? 1000 : target is Living { IsFireImmune: true } ? 0 : 6
                };
                await target.DamageAsync(Owner ?? this, damage);
                if (Type == EntityType.Snowball && damage == 0 && target is Mob mob && Owner != null && mob.IsValidTarget(Owner))
                    mob.AlertedTarget = Owner;
                if (Type == EntityType.SmallFireball && target is Living living && !living.IsFireImmune)
                    living.Ignite(5);
            }
            if (Type == EntityType.Fireball)
                await level.ExplodeAsync(this, 1, Owner);
            if (Type != EntityType.Snowball)
            {
                var fire = (Vector)(Position - Motion * 0.05f).Floor();
                if (terrain.GetBlock(fire)?.IsAir == true && terrain.GetBlock(new Vector(fire.X, fire.Y - 1, fire.Z)) is { } floor &&
                    BlockCollisionShapes.Get(floor).Count > 0)
                    await level.SetBlockAsync(fire, BlocksRegistry.Get(Material.Fire), true);
            }
            await RemoveAsync();
            return;
        }
        if (Type == EntityType.Snowball)
            Motion = Motion * 0.99f - new VectorF(0, 0.03f, 0);
        else
            Motion = (Motion + acceleration) * 0.95f;
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
    }
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<int>("ObsidianLife", age));
        tag.Set(EntityNbt.DoubleList("ObsidianAcceleration", acceleration));
        if (ownerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(ownerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (tag.TryGetTagValue<int>("ObsidianLife", out var life)) age = Math.Clamp(life, 0, 600);
        if (!EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out acceleration) && Type != EntityType.Snowball && Motion.Magnitude > 0.001f)
            acceleration = Motion / Motion.Magnitude * 0.1f;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

}

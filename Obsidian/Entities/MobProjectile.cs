using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.Nbt;

namespace Obsidian.Entities;

internal sealed class MobProjectile : Entity
{
    internal int SplashEffect { get; set; } = -1;
    internal int SplashDuration { get; set; }
    internal int SplashAmplifier { get; set; }
    internal IEntity? Owner { get; private set; }
    private Guid ownerUuid;
    private VectorD acceleration;
    private int age;
    private bool resolveOwner = true;
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(ILevel level, EntityType type)
    {
        Level = level;
        Type = type;
        Dimension = new EntityDimension { Width = type == EntityType.Fireball ? 1 : 0.3125f, Height = type == EntityType.Fireball ? 1 : 0.3125f };
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion);
    }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(Mob owner, EntityType type, VectorD position, VectorD direction) : this(owner.Level, type)
    {
        Owner = owner;
        ownerUuid = owner.Uuid;
        EntityId = Server.GetNextEntityId();
        Position = position;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (direction.Magnitude > 0.0001f)
            direction /= direction.Magnitude;
        Motion = direction * (type == EntityType.SplashPotion ? 0.75f : type == EntityType.Snowball ? 1.6f : type == EntityType.LlamaSpit ? 1.5f : 0.2f);
        acceleration = type is EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion ? VectorD.Zero : direction * 0.1f;
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion);
    }
    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), Owner?.EntityId ?? 0);
        if (Level is AbstractLevel level) level.EmitGameEvent(MobGameEvent.ProjectileShoot, Position, Owner ?? this);
    }
    public override ValueTask DamageAsync(IEntity source, float amount = 1)
    {
        if (Type == EntityType.Fireball && source.Level == Level && source.Health > 0 && amount > 0)
        {
            Deflect(source, source.GetLookDirection());
        }
        return default;
    }
    internal void Deflect(IEntity source, VectorD direction)
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
        var swept = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.3f), VectorD.Max(Position, end) + new VectorD(0.3f));
        var fraction = 1d;
        IEntity? target = null;
        foreach (var shape in terrain.GetCollisions(swept))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        foreach (var candidate in Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 3))
        {
            if (candidate is not Living || candidate.Health <= 0 || ReferenceEquals(candidate, Owner) && age < 25 ||
                candidate is IPlayer player && player.GameMode is GameMode.Creative or GameMode.Spectator)
                continue;
            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            if (MobTerrain.RayIntersection(new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f)), Position, Motion) is double hit && hit < fraction)
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
            level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
            if (Type == EntityType.SplashPotion)
            {
                await SplashAsync(target);
                await RemoveAsync();
                return;
            }
            if (target != null)
            {
                var damage = Type switch
                {
                    EntityType.SmallFireball => target is Living { IsFireImmune: true } ? 0 : 5,
                    EntityType.Snowball => target is Blaze ? 3 : 0,
                    EntityType.LlamaSpit => 1,
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
            if (Type is not EntityType.Snowball and not EntityType.LlamaSpit)
            {
                var fire = (Vector)(Position - Motion * 0.05f).Floor();
                if (terrain.GetBlock(fire)?.IsAir == true && terrain.GetBlock(new Vector(fire.X, fire.Y - 1, fire.Z)) is { } floor &&
                    BlockCollisionShapes.Get(floor).Count > 0)
                    await level.SetBlockAsync(fire, BlocksRegistry.Get(Material.Fire), true);
            }
            await RemoveAsync();
            return;
        }
        if (Type is EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion)
            Motion = Motion * 0.99f - new VectorD(0, Type == EntityType.SplashPotion ? 0.05f : 0.03f, 0);
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
        tag.Set(new NbtTag<int>("ObsidianSplashEffect", SplashEffect));
        tag.Set(new NbtTag<int>("ObsidianSplashDuration", SplashDuration));
        tag.Set(new NbtTag<int>("ObsidianSplashAmplifier", SplashAmplifier));
        if (ownerUuid != Guid.Empty) tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(ownerUuid)));
    }
    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        SplashEffect = tag.TryGetTagValue<int>("ObsidianSplashEffect", out var effect) ? effect : -1;
        SplashDuration = Math.Max(0, tag.TryGetTagValue<int>("ObsidianSplashDuration", out var duration) ? duration : 0);
        SplashAmplifier = Math.Clamp(tag.TryGetTagValue<int>("ObsidianSplashAmplifier", out var amplifier) ? amplifier : 0, 0, 10);
        if (tag.TryGetTagValue<int>("ObsidianLife", out var life)) age = Math.Clamp(life, 0, 600);
        if (!EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out acceleration) && Type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion) && Motion.Magnitude > 0.001f)
            acceleration = Motion / Motion.Magnitude * 0.1f;
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type != EntityType.SplashPotion) return;
        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(Witch.MakePotion(Material.SplashPotion, SplashEffect, SplashDuration, SplashAmplifier));
    }

    private async ValueTask SplashAsync(IEntity? directHit)
    {
        foreach (var living in Level.GetEntitiesInRange(Position, 4).OfType<Living>())
        {
            if (!living.Alive || Math.Abs(living.Position.Y - Position.Y) > 2 || living is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator }) continue;
            var distance = (living.Position - Position).Magnitude;
            if (distance >= 4) continue;
            var strength = ReferenceEquals(living, directHit) ? 1 : 1 - distance / 4;
            var undead = living.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.ZombifiedPiglin or EntityType.Zoglin or EntityType.Skeleton or EntityType.Stray or EntityType.Bogged or EntityType.Parched or EntityType.WitherSkeleton or EntityType.Wither or EntityType.Phantom or EntityType.SkeletonHorse or EntityType.ZombieHorse or EntityType.CamelHusk or EntityType.ZombieNautilus or EntityType.ZombieVillager or EntityType.Giant;
            if (SplashEffect == (int)PotionEffect.InstantDamage - 1 || SplashEffect == (int)PotionEffect.InstantHealth - 1)
            {
                var harms = (SplashEffect == (int)PotionEffect.InstantDamage - 1) != undead;
                var amount = (float)Math.Floor(strength * ((harms ? 6 : 4) << SplashAmplifier) + 0.5);
                if (harms && living is Witch && ReferenceEquals(living, Owner)) continue;
                if (harms)
                    await living.DamageAsync(Owner ?? this, living is Witch ? amount * 0.15f : amount);
                else living.Health = Math.Min(living is IPlayer ? 20 : living.GetAttributeValue("minecraft:generic.max_health"), living.Health + amount);
            }
            else
            {
                var duration = (int)(SplashDuration * strength + 0.5);
                if (duration > 20) living.AddPotionEffect(SplashEffect, duration, SplashAmplifier,
                    EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
            }
        }
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new LevelEventPacket(2002, (Vector)Position.Floor(), 0x385dc6));
    }
}

using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
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

    /// <summary>
    /// The effects a splash shatters into, when a player threw it; the mob's single effect otherwise.
    /// </summary>
    internal IReadOnlyList<PotionEffectData>? SplashEffects { get; set; }

    /// <summary>The potion item a player threw, shown to clients; a mob's splash shows its own potion.</summary>
    internal ItemStack? SplashItem { get; set; }

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
        var size = type == EntityType.Fireball ? 1 : 0.3125f;
        Dimension = new EntityDimension { Width = size, Height = size };
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion);
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(IEntity owner, EntityType type, VectorD position, VectorD direction, float? speed = null)
        : this(owner.Level, type)
    {
        Owner = owner;
        ownerUuid = owner.Uuid;
        EntityId = Server.GetNextEntityId();
        Position = position;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (direction.Magnitude > 0.0001f)
            direction /= direction.Magnitude;

        var defaultSpeed = type switch
        {
            EntityType.SplashPotion => 0.75f,
            EntityType.Snowball => 1.6f,
            EntityType.LlamaSpit => 1.5f,
            _ => 0.2f,
        };

        Motion = direction * (speed ?? defaultSpeed);
        var thrown = type is EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion;
        acceleration = thrown ? VectorD.Zero : direction * 0.1f;
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion);
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z), Owner?.EntityId ?? 0);
        if (Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.ProjectileShoot, Position, Owner ?? this);
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
            Owner = level.FindEntity(ownerUuid);

        resolveOwner = false;
        var terrain = new MobTerrain(Level);
        var blockFraction = FindBlockHitFraction(terrain);
        var (target, fraction) = FindEntityHit(blockFraction);
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;

        Position = next;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        if (fraction < 1 || target != null)
        {
            await ImpactAsync(level, terrain, target);
            return;
        }

        if (Type is EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion)
        {
            var gravity = Type == EntityType.SplashPotion ? 0.05f : 0.03f;
            Motion = Motion * 0.99f - new VectorD(0, gravity, 0);
        }
        else
        {
            Motion = (Motion + acceleration) * 0.95f;
        }

        BroadcastFlight();
    }

    /// <summary>
    /// The fraction of this tick's motion the projectile travels before it meets a block (1 when it meets none).
    /// </summary>
    private double FindBlockHitFraction(MobTerrain terrain)
    {
        var end = Position + Motion;
        var min = VectorD.Min(Position, end) - new VectorD(0.3f);
        var max = VectorD.Max(Position, end) + new VectorD(0.3f);
        var swept = new BoundingBox(min, max);
        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(swept))
        {
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        }

        return fraction;
    }

    /// <summary>
    /// The nearest living entity the projectile hits before <paramref name="fraction"/> of its motion, and the fraction
    /// it hits at (unchanged when it hits none).
    /// </summary>
    private (IEntity? Target, double Fraction) FindEntityHit(double fraction)
    {
        IEntity? target = null;
        var range = (float)Motion.Magnitude + 3;
        foreach (var candidate in Level.GetEntitiesInRange(Position, range))
        {
            if (IsIgnoredTarget(candidate))
                continue;

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            var padded = new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(padded, Position, Motion) is double hit && hit < fraction)
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

        return (target, fraction);
    }

    private bool IsIgnoredTarget(IEntity candidate) =>
        candidate is not Living
        || candidate.Health <= 0
        || ReferenceEquals(candidate, Owner) && age < 25
        || candidate is IPlayer player && player.GameMode is GameMode.Creative or GameMode.Spectator;

    /// <summary>Splashes, damages, explodes or sets fire where the projectile landed, then removes it.</summary>
    private async ValueTask ImpactAsync(AbstractLevel level, MobTerrain terrain, IEntity? target)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
        if (Type == EntityType.SplashPotion)
        {
            await SplashAsync(target);
            await RemoveAsync();
            return;
        }

        if (target != null)
            await DamageTargetAsync(target);

        if (Type == EntityType.Fireball)
            await level.ExplodeAsync(this, 1, Owner);

        if (Type is not EntityType.Snowball and not EntityType.LlamaSpit)
            await TrySetFireAsync(level, terrain);

        // Vanilla's Snowball.onHit: entity event 3, the client's snowball crack particles.
        if (Type == EntityType.Snowball)
        {
            var crack = new EntityEventPacket { EntityId = EntityId, Event = 3 };
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, crack);
        }

        await RemoveAsync();
    }

    private async ValueTask DamageTargetAsync(IEntity target)
    {
        var damage = Type switch
        {
            EntityType.SmallFireball => target is Living { IsFireImmune: true } ? 0 : 5,
            EntityType.Snowball => target is Blaze ? 3 : 0,
            EntityType.LlamaSpit => 1,
            _ => GetFireballDamage(target),
        };

        await target.DamageAsync(Owner ?? this, damage);
        if (Type == EntityType.Snowball
            && damage == 0
            && target is Mob mob
            && Owner != null
            && mob.IsValidTarget(Owner))
        {
            mob.AlertedTarget = Owner;
        }

        if (Type == EntityType.SmallFireball && target is Living living && !living.IsFireImmune)
            living.Ignite(5);
    }

    /// <summary>A ghast's fireball a player returned kills a ghast outright.</summary>
    private int GetFireballDamage(IEntity target)
    {
        if (target is Ghast && Owner is IPlayer)
            return 1000;

        return target is Living { IsFireImmune: true } ? 0 : 6;
    }

    private async ValueTask TrySetFireAsync(AbstractLevel level, MobTerrain terrain)
    {
        var fire = (Vector)(Position - Motion * 0.05f).Floor();
        var below = new Vector(fire.X, fire.Y - 1, fire.Z);
        if (terrain.GetBlock(fire)?.IsAir == true
            && terrain.GetBlock(below) is { } floor
            && BlockCollisionShapes.Get(floor).Count > 0)
        {
            await level.SetBlockAsync(fire, BlocksRegistry.Fire, true);
        }
    }

    private void BroadcastFlight()
    {
        var packet = new TeleportEntityPacket
        {
            EntityId = EntityId,
            Position = Position,
            Delta = Motion,
            Yaw = Yaw,
            Pitch = Pitch,
            OnGround = false,
        };

        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, packet);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<int>("ObsidianLife", age));
        tag.Set(EntityNbt.DoubleList("ObsidianAcceleration", acceleration));
        tag.Set(new NbtTag<int>("ObsidianSplashEffect", SplashEffect));
        tag.Set(new NbtTag<int>("ObsidianSplashDuration", SplashDuration));
        tag.Set(new NbtTag<int>("ObsidianSplashAmplifier", SplashAmplifier));
        if (ownerUuid != Guid.Empty)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(ownerUuid)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        SplashEffect = tag.TryGetNumber<int>("ObsidianSplashEffect", out var effect) ? effect : -1;
        var duration = tag.TryGetNumber<int>("ObsidianSplashDuration", out var savedDuration) ? savedDuration : 0;
        SplashDuration = Math.Max(0, duration);
        var amplifier = tag.TryGetNumber<int>("ObsidianSplashAmplifier", out var savedAmplifier) ? savedAmplifier : 0;
        SplashAmplifier = Math.Clamp(amplifier, 0, 10);
        if (tag.TryGetNumber<int>("ObsidianLife", out var life))
            age = Math.Clamp(life, 0, 600);

        var hasAcceleration = EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out acceleration);
        var accelerates = Type is not (EntityType.Snowball or EntityType.LlamaSpit or EntityType.SplashPotion);
        if (!hasAcceleration && accelerates && Motion.Magnitude > 0.001f)
            acceleration = Motion / Motion.Magnitude * 0.1f;

        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());

        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type != EntityType.SplashPotion)
            return;

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        var potion = SplashItem
            ?? Witch.MakePotion(ItemsRegistry.SplashPotion, SplashEffect, SplashDuration, SplashAmplifier);
        writer.WriteItemStack(potion);
    }

    private async ValueTask SplashAsync(IEntity? directHit)
    {
        // Vanilla's ThrownPotion.applySplash: every effect of the potion, each scaled by how near the target is.
        var effects = GetSplashEffects();
        foreach (var living in Level.GetEntitiesInRange(Position, 4).OfType<Living>())
        {
            if (IsOutOfSplash(living))
                continue;

            var distance = (living.Position - Position).Magnitude;
            if (distance >= 4)
                continue;

            var strength = ReferenceEquals(living, directHit) ? 1 : 1 - distance / 4;
            var undead = IsSplashUndead(living.Type);
            foreach (var effect in effects)
                await ApplySplashEffectAsync(living, effect, strength, undead);
        }

        var particles = new LevelEventPacket(2002, (Vector)Position.Floor(), 0x385dc6);
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, particles);
    }

    private IReadOnlyList<PotionEffectData> GetSplashEffects()
    {
        if (this.SplashEffects is { } effects)
            return effects;

        if (SplashEffect < 0)
            return [];

        var effect = new PotionEffectData
        {
            Id = SplashEffect,
            Duration = SplashDuration,
            Amplifier = SplashAmplifier,
            ShowParticles = true,
            ShowIcon = true,
        };

        return [effect];
    }

    private bool IsOutOfSplash(Living living) =>
        !living.Alive
        || Math.Abs(living.Position.Y - Position.Y) > 2
        || living is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator };

    private static bool IsSplashUndead(EntityType? type) =>
        type is EntityType.Zombie
            or EntityType.Husk
            or EntityType.Drowned
            or EntityType.ZombifiedPiglin
            or EntityType.Zoglin
            or EntityType.Skeleton
            or EntityType.Stray
            or EntityType.Bogged
            or EntityType.Parched
            or EntityType.WitherSkeleton
            or EntityType.Wither
            or EntityType.Phantom
            or EntityType.SkeletonHorse
            or EntityType.ZombieHorse
            or EntityType.CamelHusk
            or EntityType.ZombieNautilus
            or EntityType.ZombieVillager
            or EntityType.Giant;

    private async ValueTask ApplySplashEffectAsync(Living living, PotionEffectData effect, double strength, bool undead)
    {
        if (effect.Id == (int)PotionEffect.InstantDamage - 1 || effect.Id == (int)PotionEffect.InstantHealth - 1)
        {
            var harms = (effect.Id == (int)PotionEffect.InstantDamage - 1) != undead;
            var amount = (float)Math.Floor(strength * ((harms ? 6 : 4) << effect.Amplifier) + 0.5);
            if (harms && living is Witch && ReferenceEquals(living, Owner))
                return;

            if (harms)
            {
                var damage = living is Witch ? amount * 0.15f : amount;
                await living.DamageAsync(Owner ?? this, damage);
            }
            else
            {
                living.Health = Math.Min(living.GetAttributeValue("minecraft:max_health"), living.Health + amount);
            }

            return;
        }

        var duration = (int)(effect.Duration * strength + 0.5);
        if (duration > 20)
        {
            living.AddPotionEffect(effect.Id, duration, effect.Amplifier,
                EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
        }
    }
}

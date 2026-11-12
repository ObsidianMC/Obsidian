using Obsidian.API.Blocks;
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

    internal IEntity? Owner { get; set; }

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
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion);
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
        var thrown = type is EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion;
        acceleration = thrown ? VectorD.Zero : direction * 0.1f;
        NoGravity = type is not (EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion);
    }

    /// <summary>
    /// A projectile nothing shot, like a dispenser's: it moves by <paramref name="motion"/> from
    /// <paramref name="position"/>, and a fireball keeps speeding up along it.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal MobProjectile(ILevel level, EntityType type, VectorD position, VectorD motion)
        : this(level, type)
    {
        EntityId = Server.GetNextEntityId();
        Position = position;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        Motion = motion;

        var thrown = type is EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion;
        acceleration = thrown || motion.Magnitude < 0.0001f ? VectorD.Zero : motion / motion.Magnitude * 0.1f;
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

        // Vanilla's LlamaSpit runs the base tick, and is shot, before it moves; the others once they have moved.
        if (Type == EntityType.LlamaSpit)
            this.EmitShotOnce(Owner);

        var terrain = new MobTerrain(Level);
        var blockHit = ProjectileBlockHits.Find(terrain, Position, Motion);
        var (target, fraction) = FindEntityHit(blockHit?.Fraction ?? 1);
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;

        Position = next;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        this.EmitShotOnce(Owner);
        if (target != null)
        {
            await ImpactAsync(level, terrain, target);
            return;
        }

        if (blockHit is { } hit)
        {
            await HitBlockAsync(level, terrain, hit);
            return;
        }

        if (Type is EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion)
        {
            // Vanilla throwable flight applies gravity before air drag; arrows use the opposite order.
            var gravity = Type is EntityType.SplashPotion or EntityType.LingeringPotion ? 0.05 : 0.03;
            Motion = (Motion - new VectorD(0, gravity, 0)) * 0.99;
        }
        else
        {
            Motion = (Motion + acceleration) * 0.95f;
        }

        BroadcastFlight();
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

    // Vanilla's onHitBlock through the shared hit path, then what the projectile does on any hit (its onHit).
    private async ValueTask HitBlockAsync(AbstractLevel level, MobTerrain terrain, ProjectileBlockHit hit)
    {
        if (!await ProjectileBlockHits.HitAsync(level, this, Owner, hit, () => this.OnBlockHitAsync(level, hit)))
            return;

        switch (Type)
        {
            case EntityType.SplashPotion:
                await SplashAsync(null);
                break;
            case EntityType.LingeringPotion:
                this.SendPotionBreak();
                break;
            case EntityType.Fireball:
                await level.ExplodeAsync(this, 1, Owner);

                // Vanilla's explosion sets fires while mob_griefing is on; Obsidian sets one where it landed.
                if (level.LevelData.GetBooleanRule("mob_griefing"))
                    await TrySetFireAsync(level, terrain);

                break;
            case EntityType.Snowball:
                BroadcastCrack();
                break;
        }

        await RemoveAsync();
    }

    // The block reactions of vanilla's onHitBlock overrides, once the block reacted: a small fireball sets fire in
    // the empty cell in front of the face it hit, unless it's a mob's while mob_griefing is off; llama spit is gone.
    private async ValueTask OnBlockHitAsync(AbstractLevel level, ProjectileBlockHit hit)
    {
        if (Type == EntityType.LlamaSpit)
        {
            await RemoveAsync();
            return;
        }

        if (Type != EntityType.SmallFireball)
            return;

        if (Owner is Mob && !level.LevelData.GetBooleanRule("mob_griefing"))
            return;

        var fire = hit.Block + hit.Face.ToVector();
        if (!level.BlockUpdates.GetBlock(fire).IsAir)
            return;

        var state = FireBlockBehavior.StateFor(level.BlockUpdates, fire);
        level.BlockUpdates.SetBlock(fire, state, BlockUpdateFlags.All);
    }

    /// <summary>Splashes, damages, explodes or sets fire where the projectile hit an entity, then removes it.</summary>
    private async ValueTask ImpactAsync(AbstractLevel level, MobTerrain terrain, IEntity target)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
        if (Type == EntityType.SplashPotion)
        {
            await SplashAsync(target);
            await RemoveAsync();
            return;
        }

        if (Type == EntityType.LingeringPotion)
        {
            // Cloud application is still a documented gap. A lingering potion must nevertheless shatter without
            // entering the fireball damage/fire path or applying its effects as a splash.
            this.SendPotionBreak();
            await this.RemoveAsync();
            return;
        }

        await DamageTargetAsync(target);

        if (Type == EntityType.Fireball)
            await level.ExplodeAsync(this, 1, Owner);

        if (Type is not EntityType.Snowball and not EntityType.LlamaSpit)
            await TrySetFireAsync(level, terrain);

        if (Type == EntityType.Snowball)
            BroadcastCrack();

        await RemoveAsync();
    }

    // Vanilla's Snowball.onHit: entity event 3, the client's snowball crack particles.
    private void BroadcastCrack()
    {
        var crack = new EntityEventPacket { EntityId = EntityId, Event = 3 };
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, crack);
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
        if (this.Type is EntityType.SplashPotion or EntityType.LingeringPotion && this.SplashItem is ItemStack item)
            tag.Set(item.ToNbt("Item"));

        var owner = this.Owner?.Uuid ?? this.ownerUuid;
        if (owner != Guid.Empty)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(owner)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        SplashEffect = tag.TryGetNumber<int>("ObsidianSplashEffect", out var effect) ? effect : -1;
        var duration = tag.TryGetNumber<int>("ObsidianSplashDuration", out var savedDuration) ? savedDuration : 0;
        SplashDuration = Math.Max(0, duration);
        var amplifier = tag.TryGetNumber<int>("ObsidianSplashAmplifier", out var savedAmplifier) ? savedAmplifier : 0;
        SplashAmplifier = Math.Clamp(amplifier, 0, 10);
        if (this.Type is EntityType.SplashPotion or EntityType.LingeringPotion
            && tag.TryGetTag<NbtCompound>("Item", out var item))
        {
            this.SplashItem = item.ItemFromNbt();
            var contents = this.SplashItem?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
            this.SplashEffects = [.. ArrowPotionEffects.Get(contents)];
        }
        if (tag.TryGetNumber<int>("ObsidianLife", out var life))
            age = Math.Clamp(life, 0, 600);

        var hasAcceleration = EntityNbt.TryReadVector(tag, "ObsidianAcceleration", out acceleration);
        var accelerates = Type is not (EntityType.Snowball or EntityType.LlamaSpit
            or EntityType.SplashPotion or EntityType.LingeringPotion);
        if (!hasAcceleration && accelerates && Motion.Magnitude > 0.001f)
            acceleration = Motion / Motion.Magnitude * 0.1f;

        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());

        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type is not (EntityType.SplashPotion or EntityType.LingeringPotion))
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

        this.SendPotionBreak();
    }

    private void SendPotionBreak()
    {
        var effects = this.GetSplashEffects();
        var contents = this.SplashItem?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        var color = contents?.CustomColor ?? PotionColors.Mix(effects);
        // Vanilla's AbstractThrownPotion.onHit: the potion's particles (instant ones for an instant potion), then its
        // sound, which is a level event of its own.
        var block = (Vector)Position.Floor();
        // Custom instant effects still use the normal break events. Only the base potion selects the instant pair.
        var instant = contents?.Potion is Potion.Healing or Potion.StrongHealing or Potion.Harming or Potion.StrongHarming;
        var particles = new LevelEventPacket(instant ? 2007 : 2002, block, color);
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, particles);
        if (!Silent)
        {
            var sound = new LevelEventPacket(instant ? 1054 : 1053, block, 0);
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, sound);
        }
    }

    private static bool IsInstant(int effect) =>
        effect == (int)PotionEffect.InstantDamage - 1 || effect == (int)PotionEffect.InstantHealth - 1;

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
        || living.Position.Y + living.Dimension.Height <= this.Position.Y - 2
        || living.Position.Y >= this.Position.Y + this.Dimension.Height + 2
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

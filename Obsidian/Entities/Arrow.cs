namespace Obsidian.Entities;

using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.Nbt;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;

[MinecraftEntity("minecraft:arrow")]
public partial class Arrow : Entity
{
    public bool Crit { get; internal set; }
    public bool NoClip { get; protected set; }
    internal IEntity? Owner { get; set; }
    internal float Damage { get; set; } = 2;
    internal int Effect { get; set; } = -1;
    internal int EffectDuration { get; set; }
    internal int FireSeconds { get; set; }
    internal ItemStack? Weapon { get; set; }
    internal ItemStack? PickupItem { get; set; }
    internal ArrowPickup Pickup { get; set; }
    internal int PiercingLevel { get; set; }
    internal int KnockbackLevel { get; set; }
    private readonly HashSet<int> piercedEntities = [];
    private Guid ownerUuid;
    protected int age;

    /// <summary>
    /// The block an arrow entered when it stopped at <paramref name="stop"/> (on a block boundary, moving by
    /// <paramref name="motion"/>), and the face it came through.
    /// </summary>
    internal static (Vector Block, BlockFace Face) EnteredBlockFace(VectorD stop, VectorD motion)
    {
        double[] coordinates = [stop.X, stop.Y, stop.Z];
        double[] moves = [motion.X, motion.Y, motion.Z];

        // The stopping axis is the one moving whose coordinate sits on a block boundary.
        var axis = -1;
        var nearest = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            if (moves[i] == 0)
                continue;

            var distance = Math.Abs(coordinates[i] - Math.Round(coordinates[i]));
            if (distance < nearest)
            {
                nearest = distance;
                axis = i;
            }
        }

        var block = new int[3];
        for (var i = 0; i < 3; i++)
            block[i] = (int)Math.Floor(coordinates[i]);

        if (axis < 0)
            return (new Vector(block[0], block[1], block[2]), BlockFace.Up);

        // Moving positive, the arrow entered the block starting at the boundary, through its negative face; moving
        // negative, the block ending at it, through its positive face.
        var boundary = (int)Math.Round(coordinates[axis]);
        var positive = moves[axis] > 0;
        block[axis] = positive ? boundary : boundary - 1;
        var face = axis switch
        {
            0 => positive ? BlockFace.West : BlockFace.East,
            1 => positive ? BlockFace.Down : BlockFace.Up,
            _ => positive ? BlockFace.North : BlockFace.South,
        };

        return (new Vector(block[0], block[1], block[2]), face);
    }

    protected bool embedded;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull)
            return;

        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Crit ? 1 : 0) | (NoClip ? 2 : 0)));
        writer.WriteEntityMetadataType(9, EntityMetadataType.Byte);
        writer.WriteByte((byte)PiercingLevel);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(embedded);
        if (Type == EntityType.Arrow)
        {
            var color = Effect switch
            {
                1 => 0x5A6C81,
                18 => 0x4E9331,
                17 => 0x484D48,
                _ => -1,
            };

            writer.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
            writer.WriteVarInt(color);
        }
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity, additionalData);
        if (Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.ProjectileShoot, Position, Owner ?? this);
    }

    internal override IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0)
    {
        var initialVelocity = velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z);
        UpdateShotRotation(new VectorD(initialVelocity.X, initialVelocity.Y, initialVelocity.Z));
        return base.CreateSpawnPacket(initialVelocity, Owner?.EntityId ?? additionalData);
    }

    private void UpdateShotRotation(VectorD direction)
    {
        if (direction.Magnitude == 0)
            return;

        var horizontal = Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        Yaw = (float)(Math.Atan2(direction.X, direction.Z) * 180 / Math.PI);
        Pitch = (float)(Math.Atan2(direction.Y, horizontal) * 180 / Math.PI);
    }

    public override async ValueTask TickAsync()
    {
        if (Level is not AbstractLevel level || !level.IsMobTicking(Position))
            return;

        if (++age >= 1200 || Position.Y < -128)
        {
            await RemoveAsync();
            return;
        }

        if (Owner == null && ownerUuid != Guid.Empty && age % 20 == 1)
            Owner = level.FindEntity(ownerUuid);

        if (embedded)
        {
            await PickupAsync();
            return;
        }

        var terrain = new MobTerrain(Level);
        var blockFraction = FindBlockHitFraction(terrain);
        var (target, fraction) = FindEntityHit(blockFraction);
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;

        Position = next;
        UpdateShotRotation(Motion);
        BroadcastFlight();

        if (target != null)
        {
            await HitEntityAsync(level, target);
            return;
        }

        if (fraction < 1)
        {
            await HitBlockAsync(level);
            return;
        }

        var water = terrain.GetBlock((Vector)Position.Floor())?.RegistryId == BlockIds.Water;
        Motion *= GetDrag(water);
        if (!NoGravity)
            Motion -= new VectorD(0, 0.05f, 0);
    }

    /// <summary>
    /// The fraction of this tick's motion the arrow travels before it meets a block (1 when it meets none).
    /// </summary>
    private double FindBlockHitFraction(MobTerrain terrain)
    {
        var end = Position + Motion;
        var min = VectorD.Min(Position, end) - new VectorD(0.3f);
        var max = VectorD.Max(Position, end) + new VectorD(0.3f);
        var bounds = new BoundingBox(min, max);
        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(bounds))
        {
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        }

        return fraction;
    }

    /// <summary>
    /// The nearest entity the arrow hits before <paramref name="fraction"/> of its motion, and the fraction it hits at
    /// (unchanged when it hits none).
    /// </summary>
    private (IEntity? Target, double Fraction) FindEntityHit(double fraction)
    {
        IEntity? target = null;
        var range = (float)Motion.Magnitude + 20;
        foreach (var candidate in Level.GetEntitiesInRange(Position, range))
        {
            if (IsIgnoredTarget(candidate))
                continue;

            if (candidate is EnderDragon dragon)
            {
                FindDragonPartHit(dragon, ref target, ref fraction);
                continue;
            }

            var targetBounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            targetBounds = new BoundingBox(targetBounds.Min - new VectorD(0.3f), targetBounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(targetBounds, Position, Motion) is double hit && hit < fraction)
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
            and not EndCrystal
            and not MobProjectile { Type: EntityType.Fireball }
            and not VehicleEntity { IsRemoved: false }
        || ReferenceEquals(candidate, Owner)
        || candidate.Health <= 0
        || piercedEntities.Contains(candidate.EntityId)
        || candidate is IPlayer player && player.GameMode is GameMode.Creative or GameMode.Spectator;

    private void FindDragonPartHit(EnderDragon dragon, ref IEntity? target, ref double fraction)
    {
        foreach (var part in dragon.Parts)
        {
            if (MobTerrain.RayIntersection(part.BoundingBox, Position, Motion) is double partHit && partHit < fraction)
            {
                fraction = partHit;
                target = part;
            }
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

    /// <summary>Damages and affects the entity the arrow hit, then pierces it, bounces off it or ends on it.</summary>
    private async ValueTask HitEntityAsync(AbstractLevel level, IEntity target)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
        var health = target.Health;
        var accepted = (target as Living)?.AcceptedDamageCount;
        var vehicleHit = await DamageTargetAsync(target);
        var damaged = vehicleHit
            || target.Health < health
            || target is Living hitLiving && hitLiving.AcceptedDamageCount != accepted;

        await ApplyHitEffectsAsync(target, damaged);

        if (PiercingLevel > 0 && damaged && Type is EntityType.Arrow or EntityType.SpectralArrow)
        {
            piercedEntities.Add(target.EntityId);
            if (piercedEntities.Count <= PiercingLevel)
            {
                Position += Motion * 0.001;
                return;
            }
        }

        if (!damaged && Type is EntityType.Arrow or EntityType.SpectralArrow)
        {
            await BounceOffAsync();
            return;
        }

        await OnImpactAsync(target, damaged);
    }

    /// <summary>Deals the arrow's damage to <paramref name="target"/>, returning whether it hit a vehicle.</summary>
    private async ValueTask<bool> DamageTargetAsync(IEntity target)
    {
        if (target is MobProjectile fireball)
        {
            fireball.Deflect(Owner ?? this, Motion);
            return false;
        }

        var passesShulker = target is not Shulker { Closed: true } || Type != EntityType.Arrow;
        var passesWither = target is not Wither { Armored: true }
            || Type is not (EntityType.Arrow or EntityType.Trident or EntityType.SpectralArrow);
        if (!passesShulker || !passesWither)
            return false;

        var damage = GetHitDamage(target);
        if (target is EnderDragonPart part)
        {
            await part.Parent.DamagePartAsync(Owner ?? this, damage, part.Index, projectile: true);
        }
        else if (target is Living victim)
        {
            await victim.DamageCombatAsync(Owner ?? this, damage, CombatDamageKind.Projectile, Weapon, this);
        }
        // Like vanilla's VehicleEntity.hurtServer, a boat or minecart always takes the hit, and a burning
        // arrow sets off a TNT minecart.
        else if (target is VehicleEntity vehicle)
        {
            var kind = FireSeconds > 0 || Burning ? VehicleDamage.BurningProjectile : VehicleDamage.Projectile;
            await vehicle.HurtAsync(Owner ?? this, damage, kind, Motion);
            return true;
        }
        else
        {
            await target.DamageAsync(Owner ?? this, damage);
        }

        return false;
    }

    private float GetHitDamage(IEntity target)
    {
        float damage = Type switch
        {
            EntityType.ShulkerBullet => 4,
            EntityType.WitherSkull => Owner is Living ? 8 : 5,
            EntityType.Trident => 8,
            _ => (float)Math.Ceiling(Motion.Magnitude * Damage),
        };

        if (Type == EntityType.Trident)
            damage += CombatEffects.DamageBonus(Weapon, target);
        else if (Crit && Type is EntityType.Arrow or EntityType.SpectralArrow)
            damage += Globals.Random.Next((int)(damage / 2) + 2);

        return damage;
    }

    /// <summary>The effect, fire, knockback, tipped potion and post-attack effects of a hit that did damage.</summary>
    private async ValueTask ApplyHitEffectsAsync(IEntity target, bool damaged)
    {
        if (!damaged || target is not Living living)
            return;

        if (Effect >= 0)
            living.AddPotionEffect(Effect, EffectDuration, 0);

        if (FireSeconds > 0)
            living.Ignite(FireSeconds);

        if (KnockbackLevel > 0)
            CombatEffects.Knockback(living, Motion, KnockbackLevel * 0.6f);

        if (PickupItem?.Is(ItemsRegistry.TippedArrow) == true)
            await ApplyTippedEffectsAsync(living);

        if (Owner is Living attacker)
            await CombatEffects.PostAttackAsync(attacker, living, Type == EntityType.Trident ? Weapon : null);
    }

    private async ValueTask BounceOffAsync()
    {
        // Vanilla AbstractArrow.onHitEntity: Projectile.deflect(ProjectileDeflection.REVERSE) reverses the
        // motion (x -0.5) and turns the arrow 170-190 degrees, then the motion is scaled by 0.2 (net -0.1).
        // A player's arrow left with no speed is dropped for pickup and discarded.
        Motion *= -0.5;
        var turn = 170f + Globals.Random.NextSingle() * 20f;
        Yaw += turn;
        Motion *= 0.2;
        Crit = false;
        if (Owner is IPlayer && Motion.Magnitude * Motion.Magnitude < 1e-7)
        {
            if (Pickup == ArrowPickup.Allowed && PickupItem is { Count: > 0 })
                DropBlockedArrowItem();

            await RemoveAsync();
        }
    }

    private async ValueTask HitBlockAsync(AbstractLevel level)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
        if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull)
        {
            await OnImpactAsync(null, false);
            return;
        }

        // Like vanilla's onHitBlock, a block the arrow hits reacts before the arrow embeds.
        if (Level is AbstractLevel hitLevel)
        {
            var (hitBlock, hitFace) = EnteredBlockFace(Position, Motion);
            await hitLevel.OnProjectileHitBlockAsync(hitBlock, Position, hitFace);
        }

        embedded = true;
        Crit = false;
        Motion = VectorD.Zero;
        var packet = new SetEntityDataPacket { EntityId = EntityId, Entity = this };
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, packet);
    }

    protected virtual ValueTask OnImpactAsync(IEntity? target, bool damaged) => RemoveAsync();

    protected virtual double GetDrag(bool water) => water ? 0.6 : 0.99;

    private void DropBlockedArrowItem()
    {
        var entity = new ItemEntity
        {
            Level = Level,
            EntityId = Server.GetNextEntityId(),
            Position = Position,
            Item = PickupItem!,
        };

        if (Level.TryAddEntity(entity))
            entity.SpawnEntity(new Velocity(0, 0, 0));
    }

    protected async ValueTask PickupAsync()
    {
        if (Pickup == ArrowPickup.Disallowed || PickupItem is not { Count: > 0 })
            return;

        foreach (var player in Level.GetPlayersInRange(Position, 1.5f))
        {
            var mayCollect = Owner is not IPlayer
                || this is not Trident { LoyaltyLevel: > 0 }
                || Owner.Uuid == player.Uuid;
            if (mayCollect && await player.ReceiveProjectileAsync(PickupItem, Pickup == ArrowPickup.CreativeOnly))
            {
                var packet = new TakeItemEntityPacket
                {
                    CollectedEntityId = EntityId,
                    CollectorEntityId = player.EntityId,
                    PickupItemCount = 1,
                };

                PacketBroadcaster.QueuePacketToLevel(Level, packet);
                await RemoveAsync();
                break;
            }
        }
    }

    private async ValueTask ApplyTippedEffectsAsync(Living target)
    {
        var contents = PickupItem?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        foreach (var effect in ArrowPotionEffects.Get(contents))
        {
            if (effect.Id == (int)PotionEffect.InstantHealth - 1 || effect.Id == (int)PotionEffect.InstantDamage - 1)
                await ApplyInstantEffectAsync(target, effect);
            else
                target.AddPotionEffect(effect.Id, Math.Max(1, effect.Duration / 8), effect.Amplifier);
        }
    }

    private async ValueTask ApplyInstantEffectAsync(Living target, PotionEffectData effect)
    {
        var harms = (effect.Id == (int)PotionEffect.InstantDamage - 1) != CombatEffects.IsUndead(target.Type);
        if (harms)
        {
            var damage = (float)Math.Floor(0.125 * (6 << effect.Amplifier) + 0.5);
            await target.DamageCombatAsync(Owner ?? this, damage, CombatDamageKind.Magic);
        }
        else
        {
            var healing = (float)Math.Floor(0.125 * (4 << effect.Amplifier) + 0.5);
            target.Health = Math.Min(target.GetAttributeValue("minecraft:max_health"), target.Health + healing);
        }
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        var uuid = Owner?.Uuid ?? ownerUuid;
        var ownerTag = uuid == Guid.Empty ? null : new NbtArray<int>("Owner", EntityNbt.UuidToInts(uuid));
        tag.SetOrRemove("Owner", ownerTag);
        tag.Set(new NbtTag<int>("ObsidianLife", age));
        tag.Set(new NbtTag<bool>("inGround", embedded));
        tag.Set(new NbtTag<double>("damage", Damage));
        tag.Set(new NbtTag<int>("ObsidianEffect", Effect));
        tag.Set(new NbtTag<int>("ObsidianEffectDuration", EffectDuration));
        tag.Set(new NbtTag<int>("ObsidianFireSeconds", FireSeconds));
        tag.Set(new NbtTag<bool>("crit", Crit));
        tag.Set(new NbtTag<byte>("PierceLevel", (byte)PiercingLevel));
        tag.Set(new NbtTag<byte>("pickup", (byte)Pickup));
        tag.Set(new NbtTag<int>("ObsidianPunch", KnockbackLevel));
        tag.SetOrRemove("item", PickupItem?.ToNbt("item"));
        tag.SetOrRemove("weapon", Weapon?.ToNbt("weapon"));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());

        if (tag.TryGetNumber<int>("ObsidianLife", out var life))
            age = Math.Clamp(life, 0, 1200);

        embedded = tag.TryGetBool("inGround", out var ground) && ground;

        if (tag.TryGetNumber<double>("damage", out var damage) && double.IsFinite(damage))
            Damage = (float)Math.Clamp(damage, 0, float.MaxValue);

        if (tag.TryGetNumber<int>("ObsidianEffect", out var effect))
            Effect = effect;

        if (tag.TryGetNumber<int>("ObsidianEffectDuration", out var duration))
            EffectDuration = Math.Max(0, duration);

        if (tag.TryGetNumber<int>("ObsidianFireSeconds", out var fire))
            FireSeconds = Math.Clamp(fire, 0, int.MaxValue / 20);

        Crit = tag.TryGetBool("crit", out var crit) && crit;
        PiercingLevel = tag.TryGetNumber<byte>("PierceLevel", out var pierce) ? pierce : 0;
        var knownPickup = tag.TryGetNumber<byte>("pickup", out var pickup) && pickup <= 2;
        Pickup = knownPickup ? (ArrowPickup)pickup : ArrowPickup.Disallowed;
        KnockbackLevel = tag.TryGetNumber<int>("ObsidianPunch", out var punch) ? Math.Clamp(punch, 0, 255) : 0;
        PickupItem = tag.TryGetTag<NbtCompound>("item", out var item) ? item.ItemFromNbt() : null;
        Weapon = tag.TryGetTag<NbtCompound>("weapon", out var weapon) ? weapon.ItemFromNbt() : null;
        BoundingBox = Dimension.CreateBBFromPosition(Position);
    }
}

internal enum ArrowPickup : byte { Disallowed, Allowed, CreativeOnly }

[MinecraftEntity("minecraft:spectral_arrow")]
public sealed partial class SpectralArrow : Arrow
{
    public SpectralArrow()
    {
        Type = EntityType.SpectralArrow;
        Effect = (int)PotionEffect.Glowing - 1;
        EffectDuration = 200;
    }
}

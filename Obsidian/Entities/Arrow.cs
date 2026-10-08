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
    protected bool embedded;

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull) return;
        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte((byte)((Crit ? 1 : 0) | (NoClip ? 2 : 0)));
        writer.WriteEntityMetadataType(9, EntityMetadataType.Byte);
        writer.WriteByte((byte)PiercingLevel);
        writer.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        writer.WriteBoolean(embedded);
        if (Type == EntityType.Arrow)
        {
            writer.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
            writer.WriteVarInt(Effect switch { 1 => 0x5A6C81, 18 => 0x4E9331, 17 => 0x484D48, _ => -1 });
        }
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity, additionalData);
        if (Level is AbstractLevel level) level.EmitGameEvent(MobGameEvent.ProjectileShoot, Position, Owner ?? this);
    }

    internal override IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0)
    {
        var initialVelocity = velocity ?? new Velocity(Motion.X, Motion.Y, Motion.Z);
        UpdateShotRotation(new VectorD(initialVelocity.X, initialVelocity.Y, initialVelocity.Z));
        return base.CreateSpawnPacket(initialVelocity, Owner?.EntityId ?? additionalData);
    }

    private void UpdateShotRotation(VectorD direction)
    {
        if (direction.Magnitude == 0) return;
        Yaw = (float)(Math.Atan2(direction.X, direction.Z) * 180 / Math.PI);
        Pitch = (float)(Math.Atan2(direction.Y, Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z)) * 180 / Math.PI);
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
            Owner = Level.GetEntitiesInRange(Position, float.MaxValue).FirstOrDefault(entity => entity.Uuid == ownerUuid);
        if (embedded)
        {
            await PickupAsync();
            return;
        }
        var terrain = new MobTerrain(Level);
        var end = Position + Motion;
        var bounds = new BoundingBox(VectorD.Min(Position, end) - new VectorD(0.3f), VectorD.Max(Position, end) + new VectorD(0.3f));
        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(bounds))
            if (MobTerrain.RayIntersection(shape, Position, Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        IEntity? target = null;
        foreach (var candidate in Level.GetEntitiesInRange(Position, (float)Motion.Magnitude + 20))
        {
            if (candidate is not Living and not EndCrystal and not MobProjectile { Type: EntityType.Fireball } || ReferenceEquals(candidate, Owner) || candidate.Health <= 0 || piercedEntities.Contains(candidate.EntityId) ||
                candidate is IPlayer player && player.GameMode is GameMode.Creative or GameMode.Spectator)
                continue;
            if (candidate is EnderDragon dragon)
            {
                foreach (var part in dragon.Parts)
                    if (MobTerrain.RayIntersection(part.BoundingBox, Position, Motion) is double partHit && partHit < fraction)
                    { fraction = partHit; target = part; }
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
        var next = Position + Motion * fraction;
        if (!level.TryMoveEntity(this, Position, next))
            return;
        Position = next;
        UpdateShotRotation(Motion);
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TeleportEntityPacket
        { EntityId = EntityId, Position = Position, Delta = Motion, Yaw = Yaw, Pitch = Pitch, OnGround = false });
        if (target != null)
        {
            level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
            var health = target.Health;
            var accepted = (target as Living)?.AcceptedDamageCount;
            if (target is MobProjectile fireball)
                fireball.Deflect(Owner ?? this, Motion);
            else if ((target is not Shulker { Closed: true } || Type != EntityType.Arrow) &&
                (target is not Wither { Armored: true } || Type is not (EntityType.Arrow or EntityType.Trident or EntityType.SpectralArrow)))
            {
                var damage = Type == EntityType.ShulkerBullet ? 4 : Type == EntityType.WitherSkull ? Owner is Living ? 8 : 5 :
                    Type == EntityType.Trident ? 8 : (float)Math.Ceiling(Motion.Magnitude * Damage);
                if (Type == EntityType.Trident) damage += CombatEffects.DamageBonus(Weapon, target);
                else if (Crit && Type is EntityType.Arrow or EntityType.SpectralArrow)
                    damage += Globals.Random.Next((int)(damage / 2) + 2);
                if (target is EnderDragonPart part) await part.Parent.DamagePartAsync(Owner ?? this, damage, part.Index, projectile: true);
                else if (target is Living victim) await victim.DamageCombatAsync(Owner ?? this, damage, CombatDamageKind.Projectile, Weapon);
                else await target.DamageAsync(Owner ?? this, damage);
            }
            var damaged = target.Health < health || target is Living hitLiving && hitLiving.AcceptedDamageCount != accepted;
            if (damaged && Effect >= 0 && target is Living living)
                living.AddPotionEffect(Effect, EffectDuration, 0);
            if (damaged && FireSeconds > 0 && target is Living burningTarget)
                burningTarget.Ignite(FireSeconds);
            if (damaged && target is Living knocked && KnockbackLevel > 0)
                CombatEffects.Knockback(knocked, Motion, KnockbackLevel * 0.6f);
            if (damaged && target is Living potionTarget && PickupItem?.Type == Material.TippedArrow)
                await ApplyTippedEffectsAsync(potionTarget);
            if (damaged && target is Living thornTarget && Owner is Living attacker)
                await CombatEffects.PostAttackAsync(attacker, thornTarget, Type == EntityType.Trident ? Weapon : null);
            if (PiercingLevel > 0 && damaged && Type is EntityType.Arrow or EntityType.SpectralArrow)
            {
                piercedEntities.Add(target.EntityId);
                if (piercedEntities.Count <= PiercingLevel) { Position += Motion * 0.001; return; }
            }
            if (!damaged && Type is EntityType.Arrow or EntityType.SpectralArrow)
            {
                Motion *= -0.1;
                Crit = false;
                return;
            }
            await OnImpactAsync(target, damaged);
            return;
        }
        if (fraction < 1)
        {
            level.EmitGameEvent(MobGameEvent.ProjectileLand, Position, this, Owner);
            if (Type is EntityType.ShulkerBullet or EntityType.WitherSkull) { await OnImpactAsync(null, false); return; }
            embedded = true;
            Crit = false;
            Motion = VectorD.Zero;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
            return;
        }
        var water = terrain.GetBlock((Vector)Position.Floor())?.Material == Material.Water;
        Motion *= GetDrag(water);
        if (!NoGravity)
            Motion -= new VectorD(0, 0.05f, 0);
    }

    protected virtual ValueTask OnImpactAsync(IEntity? target, bool damaged) => RemoveAsync();
    protected virtual double GetDrag(bool water) => water ? 0.6 : 0.99;

    protected async ValueTask PickupAsync()
    {
        if (Pickup == ArrowPickup.Disallowed || PickupItem is not { Count: > 0 }) return;
        foreach (var player in Level.GetPlayersInRange(Position, 1.5f).OfType<Player>())
            if ((Owner is not Player || this is not Trident { LoyaltyLevel: > 0 } || Owner.Uuid == player.Uuid) &&
                await player.ReceiveProjectileAsync(PickupItem, Pickup == ArrowPickup.CreativeOnly))
            {
                PacketBroadcaster.QueuePacketToLevel(Level, new TakeItemEntityPacket
                { CollectedEntityId = EntityId, CollectorEntityId = player.EntityId, PickupItemCount = 1 });
                await RemoveAsync();
                break;
            }
    }

    private async ValueTask ApplyTippedEffectsAsync(Living target)
    {
        var contents = PickupItem?.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
        foreach (var effect in ArrowPotionEffects.Get(contents))
        {
            if (effect.Id == (int)PotionEffect.InstantHealth - 1 || effect.Id == (int)PotionEffect.InstantDamage - 1)
            {
                var harms = (effect.Id == (int)PotionEffect.InstantDamage - 1) != CombatEffects.IsUndead(target.Type);
                if (harms) await target.DamageCombatAsync(Owner ?? this, (float)Math.Floor(0.125 * (6 << effect.Amplifier) + 0.5), CombatDamageKind.Magic);
                else target.Health = Math.Min(target.GetAttributeValue("minecraft:max_health"), target.Health + (float)Math.Floor(0.125 * (4 << effect.Amplifier) + 0.5));
            }
            else target.AddPotionEffect(effect.Id, Math.Max(1, effect.Duration / 8), effect.Amplifier);
        }
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        var uuid = Owner?.Uuid ?? ownerUuid;
        tag.SetOrRemove("Owner", uuid == Guid.Empty ? null : new NbtArray<int>("Owner", EntityNbt.UuidToInts(uuid)));
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
        if (tag.TryGetTagValue<int>("ObsidianLife", out var life)) age = Math.Clamp(life, 0, 1200);
        embedded = tag.TryGetBool("inGround", out var ground) && ground;
        if (tag.TryGetTagValue<double>("damage", out var damage) && double.IsFinite(damage)) Damage = (float)Math.Clamp(damage, 0, float.MaxValue);
        if (tag.TryGetTagValue<int>("ObsidianEffect", out var effect)) Effect = effect;
        if (tag.TryGetTagValue<int>("ObsidianEffectDuration", out var duration)) EffectDuration = Math.Max(0, duration);
        if (tag.TryGetTagValue<int>("ObsidianFireSeconds", out var fire)) FireSeconds = Math.Clamp(fire, 0, int.MaxValue / 20);
        Crit = tag.TryGetBool("crit", out var crit) && crit;
        PiercingLevel = tag.TryGetTagValue<byte>("PierceLevel", out var pierce) ? pierce : 0;
        Pickup = tag.TryGetTagValue<byte>("pickup", out var pickup) && pickup <= 2 ? (ArrowPickup)pickup : ArrowPickup.Disallowed;
        KnockbackLevel = tag.TryGetTagValue<int>("ObsidianPunch", out var punch) ? Math.Clamp(punch, 0, 255) : 0;
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

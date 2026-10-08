using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public partial class Player
{
    private int damageCooldown;
    private float lastIncomingDamage;
    private int attackTicks = 1000;
    private Item? lastAttackItem;
    private VectorD? combatPosition;
    internal VectorD CombatMotion { get; private set; }
    private int shieldCooldown;
    private readonly ItemStack?[] sentEquipment = new ItemStack?[6];

    public override ValueTask DamageAsync(IEntity source, float amount = 1)
        => DamageCombatAsync(source, amount, ReferenceEquals(source, this) ? CombatDamageKind.Magic : CombatDamageKind.Melee);

    internal override async ValueTask DamageCombatAsync(IEntity source, float amount, CombatDamageKind kind, ItemStack? weapon = null)
    {
        if (!float.IsFinite(amount) || amount <= 0 || !Alive || Respawning || source.Level != Level ||
            GameMode is GameMode.Creative or GameMode.Spectator || Abilities.HasFlag(PlayerAbility.Invulnerable)) return;
        if (kind == CombatDamageKind.Fire && HasPotionEffect((int)PotionEffect.FireResistance - 1)) return;
        if (await TryBlockDamageAsync(source, amount, kind, weapon)) return;
        var incoming = amount;
        if (damageCooldown > 0)
        {
            if (incoming <= lastIncomingDamage) return;
            amount -= lastIncomingDamage;
        }
        else
        {
            damageCooldown = 10;
            HurtTime = 10;
        }
        lastIncomingDamage = incoming;
        AcceptedDamageCount++;
        if (kind is CombatDamageKind.Melee or CombatDamageKind.Projectile or CombatDamageKind.Explosion)
        {
            var armor = GetAttributeValue("minecraft:armor");
            var toughness = GetAttributeValue("minecraft:armor_toughness");
            foreach (var (item, slot) in CombatEffects.Armor(this))
            {
                armor += CombatItems.Attribute(item, "armor", slot);
                toughness += CombatItems.Attribute(item, "armor_toughness", slot);
            }
            amount = CombatItems.ReduceArmor(amount, armor, toughness,
                1 - 0.15f * CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Breach));
            for (var slot = 5; slot <= 8; slot++)
                await DamageInventoryItemAsync(slot, Math.Max(1, (int)(incoming / 4)), true);
        }
        amount = CombatEffects.Protect(this, amount, kind);
        var absorbed = Math.Min(Absorption, amount);
        Absorption -= absorbed;
        amount -= absorbed;
        if (absorbed > 0) SyncFoodMetadata();
        if (kind is CombatDamageKind.Melee or CombatDamageKind.Projectile && !ReferenceEquals(source, this))
            CombatEffects.Knockback(this, Position - source.Position, 0.4f);
        await base.DamageAsync(source, amount);
    }

    internal async ValueTask DamageInventoryItemAsync(int slot, int amount, bool armor = false)
    {
        if (GameMode == GameMode.Creative || Inventory.GetItem(slot) is not { Count: > 0 } item) return;
        var previous = item.Damage;
        var broke = CombatItems.HurtItem(item, amount, armor);
        if (broke && item.Count == 0) Inventory.RemoveItem(slot);
        if (!broke && item.Damage == previous) return;
        await SyncInventorySlotAsync(slot);
        if (broke)
            PacketBroadcaster.QueuePacketToLevel(Level, new EntityEventPacket
            { EntityId = EntityId, Event = (byte)(slot switch { 5 => 49, 6 => 50, 7 => 51, 8 => 52, 45 => 48, _ => 47 }) });
    }

    internal ValueTask SyncInventorySlotAsync(int slot) => Client.QueuePacketAsync(new ContainerSetSlotPacket
    { ContainerId = 0, Slot = (short)slot, SlotData = Inventory.GetItem(slot), StateId = Inventory.StateId++ });

    internal float AttackCharge => Math.Clamp((attackTicks + 0.5f) *
        CombatItems.Attribute(GetHeldItem(), "attack_speed", "mainhand", GetAttributeValue("minecraft:attack_speed")) / 20, 0, 1);

    internal bool CanAttack(IEntity target, ItemStack? item = null)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || target.Level != Level || ReferenceEquals(target, this)) return false;
        var eye = Position + new VectorD(0, 1.62, 0);
        var range = CombatItems.Component(item ?? GetHeldItem(), "attack_range");
        var reach = range.ValueKind == System.Text.Json.JsonValueKind.Object
            ? CombatItems.Number(range, GameMode == GameMode.Creative ? "max_creative_reach" : "max_reach", 4.5f)
            : GetAttributeValue("minecraft:entity_interaction_range") + (GameMode == GameMode.Creative ? 2 : 0);
        var minimum = CombatItems.Number(range, GameMode == GameMode.Creative ? "min_creative_reach" : "min_reach");
        var bounds = target.Dimension.CreateBBFromPosition(target.Position);
        var nearest = new VectorD(Math.Clamp(eye.X, bounds.Min.X, bounds.Max.X),
            Math.Clamp(eye.Y, bounds.Min.Y, bounds.Max.Y), Math.Clamp(eye.Z, bounds.Min.Z, bounds.Max.Z));
        var distance = (nearest - eye).Magnitude;
        return distance <= reach + 0.3f && distance >= minimum && new MobTerrain(Level).HasLineOfSight(eye, nearest);
    }

    internal (float Damage, bool Critical) GetAttackDamage(IEntity target)
    {
        var item = GetHeldItem();
        var charge = AttackCharge;
        var damage = CombatItems.Attribute(item, "attack_damage", "mainhand", GetAttributeValue("minecraft:attack_damage"));
        if (ActivePotionEffects.TryGetValue((int)PotionEffect.Strength - 1, out var strength)) damage += 3 * (strength.EffectData.Amplifier + 1);
        if (ActivePotionEffects.TryGetValue((int)PotionEffect.Weakness - 1, out var weakness)) damage -= 4 * (weakness.EffectData.Amplifier + 1);
        damage = Math.Max(0, damage) * (0.2f + charge * charge * 0.8f);
        var critical = charge > 0.9f && FallDistance > 0 && !MovementFlags.HasFlag(MovementFlags.OnGround) &&
            !Sprinting && !Swimming && Vehicle == null && !HasPotionEffect((int)PotionEffect.Blindness - 1) &&
            new MobTerrain(Level).GetBlock((Vector)Position.Floor()) is { } feet && !TagsRegistry.Block.Climbable.Entries.Contains(feet.RegistryId);
        if (critical) damage *= 1.5f;
        damage += CombatEffects.DamageBonus(item, target) * charge;
        if (item?.Type == Material.Mace && FallDistance > 1.5f && !FlyingWithElytra)
            damage += CombatItems.SmashBonus(FallDistance) + 0.5f * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Density) * FallDistance;
        return (damage, critical);
    }

    internal async ValueTask AttackAsync(PlayerAttackEntityEventArgs attack)
    {
        var target = attack.Entity;
        var damage = attack.Damage;
        var critical = attack.IsCrit;
        var item = attack.Weapon ?? GetHeldItem();
        if (!attack.ChargeAttack && !attack.PiercingAttack) (damage, critical) = GetAttackDamage(target);
        var slot = attack.WeaponSlot >= 0 ? attack.WeaponSlot : CurrentHeldItemSlot;
        if (!CanAttack(target, item) || !ReferenceEquals(Inventory.GetItem(slot), item) ||
            !attack.ChargeAttack && (damage <= 0 || !attack.PiercingAttack && item?.Holder.UnlocalizedName.EndsWith("_spear", StringComparison.Ordinal) == true && AttackCharge < 1)) return;
        if (attack.ChargeAttack && target is Living chargedTarget)
        {
            if (attack.ChargeKnockback) CombatEffects.Knockback(chargedTarget, chargedTarget.Position - Position, 0.4f);
            if (attack.ChargeDismount && chargedTarget is Player rider) rider.Vehicle?.Dismount(rider);
            if (attack.ChargeDismount && chargedTarget is Mob mob) mob.Dismount();
        }
        if (damage <= 0) return;
        var charged = AttackCharge > 0.9f;
        var smashDistance = item?.Type == Material.Mace && FallDistance > 1.5f && !FlyingWithElytra ? FallDistance : 0;
        if (!attack.PiercingAttack && !attack.ChargeAttack) attackTicks = 0;
        var health = target.Health;
        var accepted = (target as Living)?.AcceptedDamageCount;
        if (target is Living living)
            await living.DamageCombatAsync(this, damage, CombatDamageKind.Melee, item);
        else await target.DamageAsync(this, damage);
        if (target.Health >= health && (target is not Living hit || hit.AcceptedDamageCount == accepted)) return;
        AddExhaustion(0.1f);
        if (target is Living victim)
        {
            var knockback = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Knockback) + (charged && Sprinting ? 1 : 0);
            if (knockback > 0) CombatEffects.Knockback(victim, target.Position - Position, knockback * 0.5f);
            await CombatEffects.PostAttackAsync(this, victim, item);
            Wolf.AlertOwnedWolves(this, target);
            if (smashDistance > 0)
            {
                FallDistance = 0;
                foreach (var neighbor in Level.GetEntitiesInRange(target.Position, 3.5f).OfType<Living>())
                    if (!ReferenceEquals(neighbor, this) && !ReferenceEquals(neighbor, target))
                        CombatEffects.Knockback(neighbor, neighbor.Position - target.Position,
                            (float)(3.5 - (neighbor.Position - target.Position).Magnitude) * (smashDistance > 5 ? 5 : 1));
                var wind = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.WindBurst);
                if (wind > 0) ApplyImpulse(new VectorD(0, wind switch { 1 => 1.2, 2 => 1.75, _ => 2.2 }, 0));
            }
            else if (charged && !critical && !Sprinting && MovementFlags.HasFlag(MovementFlags.OnGround) &&
                CombatMotion.Magnitude < GetAttributeValue("minecraft:movement_speed") && item?.Holder.UnlocalizedName.EndsWith("_sword", StringComparison.Ordinal) == true)
            {
                var sweeping = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.SweepingEdge);
                var sweepDamage = 1 + damage * (sweeping / (sweeping + 1f));
                foreach (var neighbor in Level.GetEntitiesInRange(target.Position, 1.5f).OfType<Living>())
                    if (!ReferenceEquals(neighbor, target) && !ReferenceEquals(neighbor, this) && CanAttack(neighbor))
                    {
                        await neighbor.DamageCombatAsync(this, sweepDamage, CombatDamageKind.Melee, item);
                        CombatEffects.Knockback(neighbor, neighbor.Position - Position, 0.4f);
                    }
            }
        }
        await DamageInventoryItemAsync(slot, attack.ChargeAttack || attack.PiercingAttack || item?.Holder.UnlocalizedName.EndsWith("_sword", StringComparison.Ordinal) == true || item?.Type is Material.Trident or Material.Mace ? 1 : 2);
    }

    internal void ApplyImpulse(VectorD impulse)
    {
        Motion += impulse;
        PacketBroadcaster.QueuePacketToLevel(Level, new SetEntityMotionPacket { EntityId = EntityId, Velocity = new(Motion.X, Motion.Y, Motion.Z) });
    }

    private async ValueTask UpdateFallAsync(VectorD previous, MovementFlags flags)
    {
        var terrain = new MobTerrain(Level);
        var feet = terrain.GetBlock((Vector)Position.Floor());
        if (Vehicle != null || Abilities.HasFlag(PlayerAbility.Flying) || GameMode is GameMode.Creative or GameMode.Spectator ||
            HasPotionEffect((int)PotionEffect.Levitation - 1) || HasPotionEffect((int)PotionEffect.SlowFalling - 1) ||
            feet?.Material is Material.Water or Material.Lava or Material.Cobweb or Material.PowderSnow ||
            feet != null && TagsRegistry.Block.Climbable.Entries.Contains(feet.RegistryId))
        { FallDistance = 0; return; }
        if (previous.Y > Position.Y) FallDistance += (float)(previous.Y - Position.Y);
        if (!flags.HasFlag(MovementFlags.OnGround)) return;
        var bounds = Dimension.CreateBBFromPosition(Position);
        var supported = terrain.GetCollisions(new BoundingBox(bounds.Min - new VectorD(0, 0.05, 0), bounds.Max))
            .Any(shape => shape.Max.Y <= Position.Y + 0.001 && shape.Max.Y >= Position.Y - 0.05 &&
                shape.Max.X > bounds.Min.X && shape.Min.X < bounds.Max.X && shape.Max.Z > bounds.Min.Z && shape.Min.Z < bounds.Max.Z);
        if (!supported) return;
        var floor = terrain.GetBlock((Vector)(Position - new VectorD(0, 0.2, 0)).Floor());
        var distance = FallDistance;
        FallDistance = 0;
        if (floor?.Material == Material.SlimeBlock && !Sneaking) return;
        var jump = ActivePotionEffects.TryGetValue((int)PotionEffect.JumpBoost - 1, out var effect) ? effect.EffectData.Amplifier + 1 : 0;
        var damage = Math.Max(0, MathF.Ceiling((distance - GetAttributeValue("minecraft:safe_fall_distance") - jump) * GetAttributeValue("minecraft:fall_damage_multiplier")));
        if (floor?.Material is Material.HayBlock or Material.HoneyBlock) damage *= 0.2f;
        else if (floor != null && TagsRegistry.Block.Beds.Entries.Contains(floor.RegistryId)) damage *= 0.5f;
        await DamageCombatAsync(this, damage, CombatDamageKind.Fall);
    }

    private async ValueTask TickCombatAsync()
    {
        if (attackTicks < 1000) attackTicks++;
        if (lastAttackItem != GetHeldItem()?.Holder) { attackTicks = 0; lastAttackItem = GetHeldItem()?.Holder; }
        if (shieldCooldown > 0) shieldCooldown--;
        CombatMotion = combatPosition is VectorD previous ? Position - previous : VectorD.Zero;
        combatPosition = Position;
        await TickWeaponUseAsync();
        var slots = new[] { (int)CurrentHeldItemSlot, 45, 8, 7, 6, 5 };
        var equipment = new List<Equipment>();
        for (var index = 0; index < slots.Length; index++)
        {
            var item = Inventory.GetItem(slots[index]) ?? ItemStack.Air;
            if (sentEquipment[index] == item && sentEquipment[index]?.Count == item.Count) continue;
            sentEquipment[index] = new ItemStack(item, item.Count);
            equipment.Add(new() { Slot = (EquipmentSlot)index, Item = item });
        }
        if (equipment.Count > 0)
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEquipmentPacket { EntityId = EntityId, Equipment = equipment }, EntityId);
    }

    internal async ValueTask<int> RepairWithMendingAsync(int experience)
    {
        while (experience > 0)
        {
            var slots = new[] { (int)CurrentHeldItemSlot, 45, 5, 6, 7, 8 }.Where(slot =>
                Inventory.GetItem(slot) is { Damage: > 0 } item && CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Mending) > 0).ToArray();
            if (slots.Length == 0) break;
            var slot = slots[Globals.Random.Next(slots.Length)];
            var item = Inventory.GetItem(slot)!;
            var repaired = Math.Min(item.Damage, experience * 2);
            item[DataComponentType.Damage] = Obsidian.API.Inventory.DataComponents.ComponentBuilder.Damage with { Value = item.Damage - repaired };
            experience -= repaired / 2;
            await SyncInventorySlotAsync(slot);
        }
        return experience;
    }
}

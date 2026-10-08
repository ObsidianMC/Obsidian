using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Text.Json;

namespace Obsidian.Entities;

public partial class Player
{
    private ItemStack? usingWeapon;
    private int weaponSlot;
    private int weaponTicks;
    private int riptideTicks;
    private float riptideDamage;
    private readonly Dictionary<int, int> spearContacts = [];

    internal async ValueTask StartUsingItemAsync(InteractionHand hand)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || usingWeapon != null || eatingItem != null) return;
        var slot = hand == InteractionHand.OffHand ? 45 : CurrentHeldItemSlot;
        var item = Inventory.GetItem(slot);
        if (item is not { Count: > 0 } || item.IsAir) return;
        var armorSlot = CombatItems.PlayerArmorSlot(item);
        if (armorSlot >= 0)
        {
            var existing = Inventory.GetItem(armorSlot);
            if (CombatItems.EnchantmentLevel(existing, EnchantmentsRegistry.BindingCurse) > 0 && GameMode != GameMode.Creative) return;
            Inventory.SetItem(armorSlot, new ItemStack(item));
            Inventory.RemoveItem(slot, 1);
            if (existing is { Count: > 0 } && !existing.IsAir) Inventory.SetItem(slot, existing);
            await SyncInventorySlotAsync(slot);
            await SyncInventorySlotAsync(armorSlot);
            return;
        }
        if (item.Type == Material.Crossbow && item.GetComponent<SimpleDataComponent<ItemStack[]>>(DataComponentType.ChargedProjectiles)?.Value is { Length: > 0 } loaded)
        {
            if (await ShootCrossbowAsync(item, loaded))
            {
                item[DataComponentType.ChargedProjectiles] = ComponentBuilder.ChargedProjectiles with { Value = [] };
                await DamageInventoryItemAsync(slot, loaded.Any(projectile => projectile.Type == Material.FireworkRocket) ? 3 : 1);
                await SyncInventorySlotAsync(slot);
            }
            return;
        }
        var spear = CombatItems.Component(item, "kinetic_weapon").ValueKind == JsonValueKind.Object;
        if (item.Type is not (Material.Bow or Material.Crossbow or Material.Trident or Material.Shield) && !spear)
        { StartEating(hand); return; }
        if (item.Type is Material.Bow or Material.Crossbow && FindAmmunition(item.Type == Material.Crossbow) < 0 && GameMode != GameMode.Creative) return;
        if (item.Type == Material.Shield && shieldCooldown > 0) return;
        if (item.Type == Material.Trident && (item.Damage >= CombatItems.MaxDamage(item) - 1 ||
            CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Riptide) > 0 && !IsWet())) return;
        usingWeapon = item;
        weaponSlot = slot;
        weaponTicks = 0;
        spearContacts.Clear();
        LivingBitMask |= LivingBitMask.HandActive;
        if (hand == InteractionHand.OffHand) LivingBitMask |= LivingBitMask.ActiveHand;
        else LivingBitMask &= ~LivingBitMask.ActiveHand;
        SyncFoodMetadata();
    }

    internal void CancelWeaponUse()
    {
        if (usingWeapon == null) return;
        usingWeapon = null;
        weaponTicks = 0;
        spearContacts.Clear();
        LivingBitMask &= ~(LivingBitMask.HandActive | LivingBitMask.ActiveHand);
        SyncFoodMetadata();
    }

    private bool IsWet()
    {
        var terrain = new MobTerrain(Level);
        return terrain.GetBlock((Vector)Position.Floor())?.Material == Material.Water || terrain.IsRainingAt((Vector)Position.Floor());
    }

    private int FindAmmunition(bool crossbow)
    {
        foreach (var slot in new[] { 45, (int)CurrentHeldItemSlot }.Concat(Enumerable.Range(9, 36)).Distinct())
            if (Inventory.GetItem(slot) is { Count: > 0 } item &&
                (item.Type is Material.Arrow or Material.TippedArrow or Material.SpectralArrow || crossbow && (slot == 45 || slot == CurrentHeldItemSlot) && item.Type == Material.FireworkRocket))
                return slot;
        return -1;
    }

    private int CrossbowChargeTicks(ItemStack item)
        => Math.Max(1, (int)((1.25f - 0.25f * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.QuickCharge)) * 20));

    internal async ValueTask ReleaseUsingItemAsync()
    {
        CancelEating();
        if (usingWeapon is not { } item) return;
        var slot = weaponSlot;
        var ticks = weaponTicks;
        var valid = ReferenceEquals(Inventory.GetItem(slot), item) && item.Count > 0 && (slot == 45 || slot == CurrentHeldItemSlot) && Alive && !Respawning;
        CancelWeaponUse();
        if (!valid) return;
        if (item.Type == Material.Bow)
        {
            var draw = ticks / 20f;
            draw = Math.Min(1, (draw * draw + draw * 2) / 3);
            if (draw < 0.1f) return;
            var ammunitionSlot = FindAmmunition(false);
            var ammunition = ammunitionSlot >= 0 ? Inventory.GetItem(ammunitionSlot)! : GameMode == GameMode.Creative ? new ItemStack(ItemsRegistry.Arrow) : null;
            if (ammunition == null) return;
            var infinite = GameMode == GameMode.Creative || ammunition.Type == Material.Arrow && CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Infinity) > 0;
            if (!ShootArrow(item, ammunition, draw * 3, draw == 1, infinite ? ArrowPickup.CreativeOnly : ArrowPickup.Allowed)) return;
            if (!infinite && ammunitionSlot >= 0) { Inventory.RemoveItem(ammunitionSlot, 1); await SyncInventorySlotAsync(ammunitionSlot); }
            await DamageInventoryItemAsync(slot, 1);
        }
        else if (item.Type == Material.Trident && ticks >= 10)
        {
            var riptide = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Riptide);
            if (riptide > 0)
            {
                if (!IsWet()) return;
                await DamageInventoryItemAsync(slot, 1);
                riptideTicks = 20;
                riptideDamage = CombatItems.Attribute(item, "attack_damage", "mainhand", 0);
                LivingBitMask |= LivingBitMask.InRiptideSpinAttack;
                ApplyImpulse(GetLookDirection() * (1.5f + riptide * 0.75f) +
                    (MovementFlags.HasFlag(MovementFlags.OnGround) ? new VectorD(0, 1.2, 0) : VectorD.Zero));
                SyncFoodMetadata();
                return;
            }
            var thrown = new Trident
            {
                Level = Level, EntityId = Obsidian.Server.GetNextEntityId(), Owner = this, Position = Position + new VectorD(0, 1.52, 0),
                Motion = ShotDirection(0) * 2.5 + CombatMotion, Weapon = new ItemStack(item), PickupItem = new ItemStack(item),
                Pickup = GameMode == GameMode.Creative ? ArrowPickup.CreativeOnly : ArrowPickup.Allowed,
                LoyaltyLevel = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Loyalty)
            };
            if (!Level.TryAddEntity(thrown)) return;
            await DamageInventoryItemAsync(slot, 1);
            thrown.PickupItem = new ItemStack(item);
            if (GameMode != GameMode.Creative) { Inventory.RemoveItem(slot, 1); await SyncInventorySlotAsync(slot); }
            thrown.SpawnEntity();
        }
    }

    private VectorD ShotDirection(float angle)
    {
        VectorD direction = GetLookDirection();
        var radians = angle * Math.PI / 180;
        direction = new VectorD(direction.X * Math.Cos(radians) - direction.Z * Math.Sin(radians), direction.Y,
            direction.X * Math.Sin(radians) + direction.Z * Math.Cos(radians));
        // Vanilla's triangular inaccuracy distribution, with divergence 1.
        direction += new VectorD((Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275,
            (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275,
            (Globals.Random.NextDouble() - Globals.Random.NextDouble()) * 0.0172275);
        return direction / direction.Magnitude;
    }

    private bool ShootArrow(ItemStack weapon, ItemStack ammunition, float speed, bool critical, ArrowPickup pickup, float angle = 0, bool crossbow = false)
    {
        var arrow = new Arrow
        {
            Level = Level, EntityId = Obsidian.Server.GetNextEntityId(), Owner = this,
            Type = ammunition.Type == Material.SpectralArrow ? EntityType.SpectralArrow : EntityType.Arrow,
            Position = Position + new VectorD(0, 1.52, 0), Motion = ShotDirection(angle) * speed + (crossbow ? VectorD.Zero : CombatMotion),
            Weapon = new ItemStack(weapon), PickupItem = new ItemStack(ammunition), Pickup = pickup, Crit = critical,
            PiercingLevel = crossbow ? CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Piercing) : 0,
            KnockbackLevel = CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Punch),
            FireSeconds = CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Flame) > 0 ? 5 : 0,
            Effect = ammunition.Type == Material.SpectralArrow ? (int)PotionEffect.Glowing - 1 : -1,
            EffectDuration = ammunition.Type == Material.SpectralArrow ? 200 : 0
        };
        var power = CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Power);
        if (power > 0) arrow.Damage += power * 0.5f + 0.5f;
        if (!Level.TryAddEntity(arrow)) return false;
        arrow.SpawnEntity();
        return true;
    }

    private ValueTask<bool> ShootCrossbowAsync(ItemStack weapon, ItemStack[] ammunition)
    {
        var fired = false;
        for (var index = 0; index < ammunition.Length; index++)
        {
            var angle = index == 0 ? 0 : index % 2 == 1 ? -10 : 10;
            var round = ammunition[index];
            if (round.Type == Material.FireworkRocket)
            {
                var rocket = new FireworkRocket
                {
                    Level = Level, EntityId = Obsidian.Server.GetNextEntityId(), Owner = this, Item = new ItemStack(round),
                    Position = Position + new VectorD(0, 1.52, 0), Motion = ShotDirection(angle) * 1.6, ShotAtAngle = true
                };
                if (Level.TryAddEntity(rocket)) { rocket.SpawnEntity(); fired = true; }
            }
            else fired |= ShootArrow(weapon, round, 3.15f, true,
                GameMode == GameMode.Creative || index > 0 ? ArrowPickup.CreativeOnly : ArrowPickup.Allowed, angle, true);
        }
        return ValueTask.FromResult(fired);
    }

    private async ValueTask TickWeaponUseAsync()
    {
        if (!Alive || Respawning) { riptideTicks = 0; LivingBitMask &= ~LivingBitMask.InRiptideSpinAttack; CancelWeaponUse(); return; }
        if (riptideTicks > 0)
        {
            if (--riptideTicks == 0) { LivingBitMask &= ~LivingBitMask.InRiptideSpinAttack; SyncFoodMetadata(); }
            else foreach (var target in Level.GetEntitiesInRange(Position, 1.5f).OfType<Living>())
                if (!ReferenceEquals(target, this))
                {
                    await target.DamageCombatAsync(this, riptideDamage, CombatDamageKind.Melee, GetHeldItem());
                    riptideTicks = 0;
                    LivingBitMask &= ~LivingBitMask.InRiptideSpinAttack;
                    SyncFoodMetadata();
                    break;
                }
        }
        if (usingWeapon is not { } item) return;
        if (!Alive || Respawning || GameMode == GameMode.Spectator || !ReferenceEquals(Inventory.GetItem(weaponSlot), item) || item.Count <= 0 || weaponSlot != 45 && weaponSlot != CurrentHeldItemSlot)
        { CancelWeaponUse(); return; }
        weaponTicks++;
        if (item.Type == Material.Crossbow && weaponTicks >= CrossbowChargeTicks(item))
        {
            var slot = FindAmmunition(true);
            var ammunition = slot >= 0 ? Inventory.GetItem(slot)! : GameMode == GameMode.Creative ? new ItemStack(ItemsRegistry.Arrow) : null;
            if (ammunition == null) { CancelWeaponUse(); return; }
            var count = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Multishot) > 0 ? 3 : 1;
            item[DataComponentType.ChargedProjectiles] = ComponentBuilder.ChargedProjectiles with
            { Value = Enumerable.Range(0, count).Select(_ => new ItemStack(ammunition)).ToArray() };
            if (GameMode != GameMode.Creative && slot >= 0) { Inventory.RemoveItem(slot, 1); await SyncInventorySlotAsync(slot); }
            await SyncInventorySlotAsync(weaponSlot);
            CancelWeaponUse();
        }
        else if (CombatItems.Component(item, "kinetic_weapon") is { ValueKind: JsonValueKind.Object } kinetic)
            await TickSpearAsync(item, kinetic);
    }

    private IEnumerable<Living> SpearTargets(ItemStack item)
    {
        var range = CombatItems.Component(item, "attack_range");
        var reach = CombatItems.Number(range, GameMode == GameMode.Creative ? "max_creative_reach" : "max_reach", 4.5f);
        var minimum = CombatItems.Number(range, GameMode == GameMode.Creative ? "min_creative_reach" : "min_reach", 2);
        var eye = Position + new VectorD(0, 1.62, 0);
        var direction = GetLookDirection();
        var start = eye + direction * minimum;
        var ray = direction * (reach - minimum);
        var margin = new VectorD(CombatItems.Number(range, "hitbox_margin", 0.125f));
        return Level.GetEntitiesInRange(Position, reach + 2).OfType<Living>().Where(target => !ReferenceEquals(target, this) && target.Alive &&
            MobTerrain.RayIntersection(new BoundingBox(target.Dimension.CreateBBFromPosition(target.Position).Min - margin,
                target.Dimension.CreateBBFromPosition(target.Position).Max + margin), start, ray) != null && CanAttack(target, item));
    }

    internal async ValueTask StabAsync()
    {
        var item = GetHeldItem();
        if (item == null || CombatItems.Component(item, "piercing_weapon").ValueKind != JsonValueKind.Object || AttackCharge < 1 || !Alive || Respawning || GameMode == GameMode.Spectator) return;
        try
        {
            foreach (var target in SpearTargets(item).ToArray())
            {
                var (damage, critical) = GetAttackDamage(target);
                await EventDispatcher.ExecuteEventAsync(new PlayerAttackEntityEventArgs(this, target, Server, Sneaking)
                { Damage = damage, IsCrit = critical, Weapon = item, WeaponSlot = CurrentHeldItemSlot, PiercingAttack = true });
            }
        }
        finally { attackTicks = 0; }
        var lunge = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Lunge);
        if (lunge > 0 && FoodLevel >= 6 && !IsWet() && !FlyingWithElytra && Vehicle == null)
        {
            var look = GetLookDirection();
            ApplyImpulse(new VectorD(look.X, 0, look.Z) * (0.458f * lunge));
            AddExhaustion(4 * lunge);
            await DamageInventoryItemAsync(CurrentHeldItemSlot, 1);
        }
    }

    private async ValueTask TickSpearAsync(ItemStack item, JsonElement kinetic)
    {
        var elapsed = weaponTicks - (int)CombatItems.Number(kinetic, "delay_ticks");
        if (elapsed < 0) return;
        var look = GetLookDirection();
        var ownMotion = Vehicle?.Motion ?? CombatMotion;
        var speed = (ownMotion.X * look.X + ownMotion.Y * look.Y + ownMotion.Z * look.Z) * 20;
        foreach (var target in SpearTargets(item).ToArray())
        {
            if (spearContacts.TryGetValue(target.EntityId, out var tick) && weaponTicks - tick < CombatItems.Number(kinetic, "contact_cooldown_ticks", 10)) continue;
            spearContacts[target.EntityId] = weaponTicks;
            var targetMotion = target is Player player ? player.CombatMotion : target.Motion;
            var relative = Math.Max(0, speed - (targetMotion.X * look.X + targetMotion.Y * look.Y + targetMotion.Z * look.Z) * 20);
            bool Meets(string name) => kinetic.TryGetProperty(name, out var condition) &&
                elapsed <= CombatItems.Number(condition, "max_duration_ticks") &&
                speed >= CombatItems.Number(condition, "min_speed") && relative >= CombatItems.Number(condition, "min_relative_speed");
            var damage = Meets("damage_conditions");
            var knockback = Meets("knockback_conditions");
            var dismount = Meets("dismount_conditions");
            if (damage || knockback || dismount)
            {
                var amount = CombatItems.Attribute(item, "attack_damage", weaponSlot == 45 ? "offhand" : "mainhand", 1) +
                    (float)Math.Floor(relative * CombatItems.Number(kinetic, "damage_multiplier", 1)) + CombatEffects.DamageBonus(item, target);
                var attack = new PlayerAttackEntityEventArgs(this, target, Server, Sneaking)
                {
                    Damage = damage ? amount : 0, Weapon = item, WeaponSlot = weaponSlot,
                    ChargeAttack = true, ChargeKnockback = knockback, ChargeDismount = dismount
                };
                // Charge damage has its own per-target contact cooldown, independent of jab cooldown.
                await EventDispatcher.ExecuteEventAsync(attack);
            }
        }
    }

    private async ValueTask<bool> TryBlockDamageAsync(IEntity source, float amount, CombatDamageKind kind, ItemStack? weapon)
    {
        if (usingWeapon?.Type != Material.Shield || weaponTicks < 5 || shieldCooldown > 0 ||
            kind is not (CombatDamageKind.Melee or CombatDamageKind.Projectile or CombatDamageKind.Explosion)) return false;
        if (kind == CombatDamageKind.Projectile && CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.Piercing) > 0) return false;
        var incoming = source.Position - Position;
        var look = GetLookDirection();
        if (incoming.X * look.X + incoming.Z * look.Z <= 0) return false;
        if (amount >= 3) await DamageInventoryItemAsync(weaponSlot, 1 + (int)Math.Floor(amount));
        if (kind == CombatDamageKind.Melee && source is Living attacker && weapon?.Holder.UnlocalizedName.EndsWith("_axe", StringComparison.Ordinal) == true)
        {
            shieldCooldown = 100;
            CancelWeaponUse();
            await Client.QueuePacketAsync(new EntityEventPacket { EntityId = EntityId, Event = 30 });
        }
        else await Client.QueuePacketAsync(new EntityEventPacket { EntityId = EntityId, Event = 29 });
        return true;
    }

    internal async ValueTask<bool> ReceiveProjectileAsync(ItemStack item, bool creativeOnly)
    {
        if (!Alive || Respawning || GameMode == GameMode.Spectator || creativeOnly && GameMode != GameMode.Creative) return false;
        if (creativeOnly) return true;
        for (var pass = 0; pass < 2; pass++)
            foreach (var slot in Enumerable.Range(36, 9).Concat(Enumerable.Range(9, 27)))
            {
                var existing = Inventory.GetItem(slot);
                var empty = existing.IsNullOrAir() || existing.Count <= 0;
                if (pass == 0 ? empty || existing != item || existing.Count >= existing.MaxStackSize : !empty) continue;
                if (empty) Inventory.SetItem(slot, new ItemStack(item));
                else existing.Count++;
                await SyncInventorySlotAsync(slot);
                return true;
            }
        return false;
    }
}

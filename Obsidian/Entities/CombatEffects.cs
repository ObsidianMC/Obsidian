using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

internal static class CombatEffects
{
    internal static IEnumerable<(ItemStack Item, string Slot)> Armor(Living entity)
    {
        if (entity is Player player)
        {
            for (var index = 0; index < 4; index++)
                if (player.Inventory.GetItem(5 + index) is { Count: > 0 } item)
                    yield return (item, index switch { 0 => "head", 1 => "chest", 2 => "legs", _ => "feet" });
        }
        else if (entity is Mob mob)
        {
            foreach (var slot in new[] { EquipmentSlot.Helmet, EquipmentSlot.Chestplate, EquipmentSlot.Leggings, EquipmentSlot.Boots, EquipmentSlot.Body })
            {
                var item = mob.GetEquipment(slot);
                if (!item.IsAir && item.Count > 0)
                    yield return (item, slot switch { EquipmentSlot.Helmet => "head", EquipmentSlot.Chestplate => "chest", EquipmentSlot.Leggings => "legs", EquipmentSlot.Boots => "feet", _ => "body" });
            }
        }
    }

    internal static float Protect(Living entity, float amount, CombatDamageKind kind)
    {
        if (kind is CombatDamageKind.Void or CombatDamageKind.Starvation) return amount;
        if (kind == CombatDamageKind.Fire && entity.HasPotionEffect((int)PotionEffect.FireResistance - 1)) return 0;
        if (entity.ActivePotionEffects.TryGetValue((int)PotionEffect.Resistance - 1, out var resistance))
            amount *= Math.Max(0, 1 - 0.2f * (resistance.EffectData.Amplifier + 1));
        var protection = 0;
        foreach (var (item, _) in Armor(entity))
        {
            protection += CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Protection);
            protection += kind switch
            {
                CombatDamageKind.Fall => 3 * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.FeatherFalling),
                CombatDamageKind.Fire => 2 * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.FireProtection),
                CombatDamageKind.Explosion => 2 * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.BlastProtection),
                CombatDamageKind.Projectile => 2 * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.ProjectileProtection), _ => 0
            };
        }
        return amount * (1 - Math.Clamp(protection, 0, 20) / 25f);
    }

    internal static float DamageBonus(ItemStack? item, IEntity target)
    {
        var sharpness = CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Sharpness);
        var bonus = sharpness > 0 ? 0.5f * sharpness + 0.5f : 0;
        if (IsUndead(target.Type))
            bonus += 2.5f * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Smite);
        if (IsArthropod(target.Type))
            bonus += 2.5f * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.BaneOfArthropods);
        if (IsAquatic(target.Type))
            bonus += 2.5f * CombatItems.EnchantmentLevel(item, EnchantmentsRegistry.Impaling);
        return bonus;
    }

    internal static async ValueTask PostAttackAsync(Living attacker, Living target, ItemStack? weapon)
    {
        var fire = CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.FireAspect);
        if (fire > 0) target.Ignite(fire * 4);
        var bane = CombatItems.EnchantmentLevel(weapon, EnchantmentsRegistry.BaneOfArthropods);
        if (bane > 0 && IsArthropod(target.Type))
            target.AddPotionEffect((int)PotionEffect.Slowness - 1, 20 + Globals.Random.Next(10 * bane), 3);
        foreach (var (armor, slot) in Armor(target).ToArray())
        {
            var thorns = CombatItems.EnchantmentLevel(armor, EnchantmentsRegistry.Thorns);
            if (thorns <= 0 || Globals.Random.NextSingle() >= thorns * 0.15f) continue;
            await attacker.DamageCombatAsync(target, thorns > 10 ? thorns - 10 : Globals.Random.Next(1, 5), CombatDamageKind.Magic);
            if (target is Player player)
                await player.DamageInventoryItemAsync(slot switch { "head" => 5, "chest" => 6, "legs" => 7, _ => 8 }, 2, true);
            else if (target is Mob mob)
                mob.DamageEquipment(slot switch { "head" => EquipmentSlot.Helmet, "chest" => EquipmentSlot.Chestplate, "legs" => EquipmentSlot.Leggings, "feet" => EquipmentSlot.Boots, _ => EquipmentSlot.Body }, 2);
        }
    }

    internal static bool IsUndead(EntityType type) => type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or
        EntityType.ZombieVillager or EntityType.ZombifiedPiglin or EntityType.Zoglin or EntityType.Skeleton or EntityType.Stray or
        EntityType.Bogged or EntityType.Parched or EntityType.WitherSkeleton or EntityType.Wither or EntityType.Phantom or
        EntityType.SkeletonHorse or EntityType.ZombieHorse or EntityType.ZombieNautilus or EntityType.CamelHusk or EntityType.Giant;

    internal static bool IsArthropod(EntityType type) => type is EntityType.Spider or EntityType.CaveSpider or EntityType.Silverfish or EntityType.Endermite or EntityType.Bee;

    internal static bool IsAquatic(EntityType type) => type is EntityType.Axolotl or EntityType.Cod or EntityType.Salmon or
        EntityType.Pufferfish or EntityType.TropicalFish or EntityType.Squid or EntityType.GlowSquid or EntityType.Turtle or
        EntityType.Dolphin or EntityType.Guardian or EntityType.ElderGuardian or EntityType.Nautilus;

    internal static void Knockback(Living target, VectorD direction, float strength)
    {
        var resistance = target.GetAttributeValue("minecraft:knockback_resistance") +
            Armor(target).Sum(entry => CombatItems.Attribute(entry.Item, "knockback_resistance", entry.Slot));
        strength *= 1 - Math.Clamp(resistance, 0, 1);
        var horizontal = Math.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
        if (strength <= 0 || horizontal < 0.00001) return;
        target.Motion = new VectorD(target.Motion.X / 2 + direction.X / horizontal * strength,
            target.MovementFlags.HasFlag(MovementFlags.OnGround) ? Math.Min(0.4, target.Motion.Y / 2 + strength) : target.Motion.Y,
            target.Motion.Z / 2 + direction.Z / horizontal * strength);
        var packet = new SetEntityMotionPacket { EntityId = target.EntityId, Velocity = new(target.Motion.X, target.Motion.Y, target.Motion.Z) };
        target.PacketBroadcaster.QueuePacketToLevelInRange(target.Level, target.Position, packet);
    }
}

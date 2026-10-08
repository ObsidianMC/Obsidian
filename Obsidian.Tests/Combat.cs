using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

public sealed class Combat
{
    [Theory]
    [InlineData(Material.IronSword, 6, 1.6)]
    [InlineData(Material.Trident, 9, 1.1)]
    [InlineData(Material.Mace, 6, 0.6)]
    [InlineData(Material.IronSpear, 3, 1.0526316)]
    public void WeaponStatsComeFromVanillaComponents(Material material, float damage, float speed)
    {
        var item = ItemsRegistry.GetSingleItem(material);
        Assert.Equal(damage, CombatItems.Attribute(item, "attack_damage", "mainhand", 1), 4);
        Assert.Equal(speed, CombatItems.Attribute(item, "attack_speed", "mainhand", 4), 4);
    }

    [Fact]
    public void ExplicitlyEmptyModifiersOverrideItemDefaults()
    {
        var item = new ItemStack(ItemsRegistry.IronSword, 1,
            ComponentBuilder.AttributeModifiers with { Value = [] });
        Assert.Equal(1, CombatItems.Attribute(item, "attack_damage", "mainhand", 1));
    }

    [Theory]
    [InlineData(10, 20, 0, 4)]
    [InlineData(10, 20, 8, 3)]
    [InlineData(100, 20, 0, 84)]
    [InlineData(10, 0, 0, 10)]
    public void ArmorUsesDamageAndToughness(float damage, float armor, float toughness, float expected)
        => Assert.Equal(expected, CombatItems.ReduceArmor(damage, armor, toughness), 4);

    [Theory]
    [InlineData(3, 0, 0)]
    [InlineData(3.1, 0, 1)]
    [InlineData(5, 1, 1)]
    [InlineData(10, 0, 7)]
    public void FallingUsesCeilingAndJumpBoost(float distance, int jumpBoost, float expected)
        => Assert.Equal(expected, CombatItems.FallDamage(distance, jumpBoost));

    [Theory]
    [InlineData(2, 8)]
    [InlineData(4, 14)]
    [InlineData(10, 24)]
    public void MaceSmashUsesPiecewiseFallBonus(float distance, float expected)
        => Assert.Equal(expected, CombatItems.SmashBonus(distance));

    [Fact]
    public void StoredEnchantmentsDoNotGrantCombatEffects()
    {
        var book = new ItemStack(ItemsRegistry.EnchantedBook);
        EnchantmentHelper.Enchant(book, EnchantmentsRegistry.Sharpness, 5);
        Assert.Equal(0, CombatItems.EnchantmentLevel(book, EnchantmentsRegistry.Sharpness));
    }

    [Fact]
    public void ArmorProtectionIsCappedAndDoesNotProtectAgainstStarvation()
    {
        var mob = new Zombie { Level = null! };
        foreach (var slot in new[] { EquipmentSlot.Helmet, EquipmentSlot.Chestplate, EquipmentSlot.Leggings, EquipmentSlot.Boots })
        {
            var item = ItemsRegistry.GetSingleItem(slot switch { EquipmentSlot.Helmet => Material.DiamondHelmet,
                EquipmentSlot.Chestplate => Material.DiamondChestplate, EquipmentSlot.Leggings => Material.DiamondLeggings, _ => Material.DiamondBoots });
            EnchantmentHelper.Enchant(item, EnchantmentsRegistry.Protection, 10);
            mob.SetEquipment(slot, item);
        }
        Assert.Equal(2, CombatEffects.Protect(mob, 10, CombatDamageKind.Melee), 4);
        Assert.Equal(10, CombatEffects.Protect(mob, 10, CombatDamageKind.Starvation));
    }

    [Fact]
    public void FeatherFallingOnlyProtectsAgainstFalls()
    {
        var mob = new Zombie { Level = null! };
        var boots = ItemsRegistry.GetSingleItem(Material.DiamondBoots);
        EnchantmentHelper.Enchant(boots, EnchantmentsRegistry.FeatherFalling, 4);
        mob.SetEquipment(EquipmentSlot.Boots, boots);
        Assert.Equal(5.2f, CombatEffects.Protect(mob, 10, CombatDamageKind.Fall), 4);
        Assert.Equal(10, CombatEffects.Protect(mob, 10, CombatDamageKind.Projectile));
    }

    [Fact]
    public void DurabilityFallsBackToTheItemsVanillaMaximum()
    {
        var sword = ItemsRegistry.GetSingleItem(Material.IronSword);
        sword[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = sword.Holder.MaxDamage - 1 };
        Assert.True(CombatItems.HurtItem(sword, 1));
        Assert.Equal(0, sword.Count);
    }

    [Fact]
    public void ChargedCrossbowsKeepTheirAmmunitionAcrossSaving()
    {
        var ammunition = new ItemStack(ItemsRegistry.TippedArrow, 1,
            new PotionContentsDataComponent { Potion = Potion.StrongPoison });
        var crossbow = new ItemStack(ItemsRegistry.Crossbow, 1,
            ComponentBuilder.ChargedProjectiles with { Value = [ammunition] });
        var restored = crossbow.ToNbt().ItemFromNbt()!;
        var loaded = Assert.Single(restored.GetComponent<SimpleDataComponent<ItemStack[]>>(DataComponentType.ChargedProjectiles)!.Value!);
        Assert.Equal(Material.TippedArrow, loaded.Type);
        Assert.Equal(Potion.StrongPoison, loaded.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)!.Potion);
    }

    [Fact]
    public void TridentsKeepLoyaltyDamageAndOwnershipAcrossSaving()
    {
        var item = ItemsRegistry.GetSingleItem(Material.Trident);
        EnchantmentHelper.Enchant(item, EnchantmentsRegistry.Loyalty, 3);
        item[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 42 };
        var owner = new Zombie { Level = null! };
        var trident = new Trident { Level = null!, Owner = owner, PickupItem = item, Weapon = new ItemStack(item), Pickup = ArrowPickup.Allowed, LoyaltyLevel = 3 };
        var tag = new Obsidian.Nbt.NbtCompound();
        trident.WriteNbt(tag);
        var restored = new Trident { Level = null! };
        restored.ReadNbt(tag);
        Assert.Equal(3, restored.LoyaltyLevel);
        Assert.Equal(42, restored.PickupItem!.Damage);
        Assert.Equal(ArrowPickup.Allowed, restored.Pickup);
        Assert.True(tag.TryGetTag<Obsidian.Nbt.NbtArray<int>>("Owner", out var savedOwner));
        Assert.Equal(EntityNbt.UuidToInts(owner.Uuid), savedOwner.GetArray());
    }

    [Fact]
    public void FifteenBookshelvesGuaranteeThirtyLevelBottomOffer()
    {
        for (var seed = 0; seed < 64; seed++)
        {
            var random = new LegacyRandomSource(seed);
            Obsidian.Entities.Player.EnchantingCost(random, 0, 15);
            Obsidian.Entities.Player.EnchantingCost(random, 1, 15);
            Assert.Equal(30, Obsidian.Entities.Player.EnchantingCost(random, 2, 15));
        }
    }

    [Fact]
    public void TippedArrowsRetainStrongPotionAmplifiers()
    {
        var effect = Assert.Single(ArrowPotionEffects.Get(new PotionContentsDataComponent { Potion = Potion.StrongPoison }));
        Assert.Equal((int)PotionEffect.Poison - 1, effect.Id);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(54, effect.Duration / 8);
    }
}

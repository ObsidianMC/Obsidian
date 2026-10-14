using System.Collections.Immutable;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class Combat
{
    [Fact]
    public async Task EquipmentModifiersFollowVanillaOrderAndSync()
    {
        await using var context = TestServerFactory.CreateServer($"attributes-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Attributes");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        var client = (TestClient)player.Client;
        Assert.True(VanillaAttributes.TryGetProtocolId("minecraft:movement_speed", out var speedId));

        // A held item adding to movement speed with each operation, and armour past vanilla's maximum, and a worn
        // diamond helmet's default armour and toughness.
        AttributeModifier Modifier(int id, string name, double value, AttributeOperation operation) => new()
        {
            Id = id,
            Name = name,
            Value = value,
            Operation = operation,
            Slot = AttributeSlot.MainHand
        };

        var charm = new ItemStack(ItemsRegistry.Stick, 1, ComponentBuilder.AttributeModifiers with
        {
            Value =
            [
                Modifier(speedId, "test:total", 1, AttributeOperation.MulTotal),
                Modifier(speedId, "test:base", 0.5, AttributeOperation.MulBase),
                Modifier(speedId, "test:add", 0.05, AttributeOperation.Add),
                Modifier(AttributeIds.Armor, "test:armor", 40, AttributeOperation.Add)
            ]
        });
        player.Inventory.SetItem(player.CurrentHeldItemSlot, charm);
        player.Inventory.SetItem(5, ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondHelmet));
        player.UpdateEquipmentAttributes();
        client.ClearPackets();

        // Vanilla's order: (0.1 + 0.05) * (1 + 0.5) * (1 + 1); armour is clamped to 30.
        Assert.Equal(0.45, player.GetAttributeValue("minecraft:movement_speed"), 5);
        Assert.Equal(30, player.GetAttributeValue("minecraft:armor"));
        Assert.Equal(2, player.GetAttributeValue("minecraft:armor_toughness"));

        player.Sprinting = true;
        player.UpdateEquipmentAttributes();
        Assert.Equal(0.585, player.GetAttributeValue("minecraft:movement_speed"), 5);

        // The changes reach the player as vanilla's packet: the base and every modifier of each changed attribute.
        player.SendAttributeChanges();
        var packet = Assert.Single(client.SentPackets.OfType<UpdateAttributesPacket>());
        using var buffer = new NetworkBuffer();
        packet.Serialize(buffer);
        using var reader = new NetworkBuffer(buffer.AsSpan(0, buffer.Size).ToArray());
        Assert.Equal(player.EntityId, reader.ReadVarInt());

        var sent = new Dictionary<int, (double Base, Dictionary<string, (double, int)> Modifiers)>();
        for (var count = reader.ReadVarInt(); count > 0; count--)
        {
            var id = reader.ReadVarInt();
            var baseValue = reader.ReadDouble();
            var modifiers = new Dictionary<string, (double, int)>();

            for (var entries = reader.ReadVarInt(); entries > 0; entries--)
                modifiers.Add(reader.ReadString(), (reader.ReadDouble(), reader.ReadVarInt()));

            sent.Add(id, (baseValue, modifiers));
        }

        Assert.Equal(new[] { AttributeIds.Armor, AttributeIds.ArmorToughness, speedId }, sent.Keys.Order());
        Assert.Equal(0.1f, sent[speedId].Base);
        Assert.Equal((0.05, 0), sent[speedId].Modifiers["test:add"]);
        Assert.Equal((0.5, 1), sent[speedId].Modifiers["test:base"]);
        Assert.Equal((1d, 2), sent[speedId].Modifiers["test:total"]);
        Assert.Equal((0.3, 2), sent[speedId].Modifiers["minecraft:sprinting"]);
        Assert.Equal((3d, 0), sent[AttributeIds.Armor].Modifiers["minecraft:armor.helmet"]);

        // Putting the item away takes its modifiers off.
        player.Inventory.SetItem(player.CurrentHeldItemSlot, null);
        player.UpdateEquipmentAttributes();
        Assert.Equal(0.13, player.GetAttributeValue("minecraft:movement_speed"), 5);
        Assert.Equal(3, player.GetAttributeValue("minecraft:armor"));
    }

    [Theory]
    [InlineData("minecraft:iron_sword", 6, 1.6)]
    [InlineData("minecraft:iron_spear", 3, 1.0526316)]
    public void WeaponStatsComeFromVanillaComponents(string identifier, float damage, float speed)
    {
        var item = ItemsRegistry.GetSingleItem(ItemsRegistry.Get(identifier));
        Assert.Equal(damage, CombatItems.Attribute(item, "attack_damage", "mainhand", 1), 4);
        Assert.Equal(speed, CombatItems.Attribute(item, "attack_speed", "mainhand", 4), 4);
    }

    [Fact]
    public void CombatComponentsOverrideDefaults()
    {
        {
            var item = new ItemStack(ItemsRegistry.IronSword, 1,
                ComponentBuilder.AttributeModifiers with { Value = [] });
            Assert.Equal(1, CombatItems.Attribute(item, "attack_damage", "mainhand", 1));
        }

        {
            var book = new ItemStack(ItemsRegistry.EnchantedBook);
            EnchantmentHelper.Enchant(book, EnchantmentsRegistry.Sharpness, 5);
            Assert.Equal(0, CombatItems.EnchantmentLevel(book, EnchantmentsRegistry.Sharpness));
        }

        {
            var sword = ItemsRegistry.GetSingleItem(ItemsRegistry.IronSword);
            sword[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = sword.Holder.MaxDamage - 1 };
            Assert.True(CombatItems.HurtItem(sword, 1));
            Assert.Equal(0, sword.Count);
        }
    }

    [Theory]
    [InlineData(10, 20, 8, 3)]
    [InlineData(100, 20, 0, 84)]
    public void ArmorUsesDamageAndToughness(float damage, float armor, float toughness, float expected)
        => Assert.Equal(expected, CombatItems.ReduceArmor(damage, armor, toughness), 4);

    [Theory]
    [InlineData(3.1, 0, 1)]
    [InlineData(5, 1, 1)]
    public void FallingUsesCeilingAndJumpBoost(float distance, int jumpBoost, float expected)
        => Assert.Equal(expected, CombatItems.FallDamage(distance, jumpBoost));

    [Theory]
    [InlineData(2, 8)]
    [InlineData(4, 14)]
    [InlineData(10, 24)]
    public void MaceSmashUsesPiecewiseFallBonus(float distance, float expected)
        => Assert.Equal(expected, CombatItems.SmashBonus(distance));

    [Fact]
    public void ArmorEnchantmentsRespectDamageKinds()
    {
        {
            var mob = new Zombie { Level = null! };
            EquipmentSlot[] slots =
            [
                EquipmentSlot.Helmet,
                EquipmentSlot.Chestplate,
                EquipmentSlot.Leggings,
                EquipmentSlot.Boots
            ];
            foreach (var slot in slots)
            {
                var item = ItemsRegistry.GetSingleItem(slot switch
                {
                    EquipmentSlot.Helmet => ItemsRegistry.DiamondHelmet,
                    EquipmentSlot.Chestplate => ItemsRegistry.DiamondChestplate,
                    EquipmentSlot.Leggings => ItemsRegistry.DiamondLeggings,
                    _ => ItemsRegistry.DiamondBoots
                });
                EnchantmentHelper.Enchant(item, EnchantmentsRegistry.Protection, 10);
                mob.SetEquipment(slot, item);
            }
            Assert.Equal(2, CombatEffects.Protect(mob, 10, CombatDamageKind.Melee), 4);
            Assert.Equal(10, CombatEffects.Protect(mob, 10, CombatDamageKind.Starvation));
            Assert.Equal(10, CombatEffects.Protect(mob, 10, CombatDamageKind.SonicBoom));
        }

        {
            var mob = new Zombie { Level = null! };
            var boots = ItemsRegistry.GetSingleItem(ItemsRegistry.DiamondBoots);
            EnchantmentHelper.Enchant(boots, EnchantmentsRegistry.FeatherFalling, 4);
            mob.SetEquipment(EquipmentSlot.Boots, boots);
            Assert.Equal(5.2f, CombatEffects.Protect(mob, 10, CombatDamageKind.Fall), 4);
            Assert.Equal(10, CombatEffects.Protect(mob, 10, CombatDamageKind.Projectile));
        }
    }

    [Fact]
    public void ProjectileItemsPreserveCombatState()
    {
        {
            var ammunition = new ItemStack(ItemsRegistry.TippedArrow, 1,
                new PotionContentsDataComponent { Potion = Potion.StrongPoison });
            var crossbow = new ItemStack(ItemsRegistry.Crossbow, 1,
                ComponentBuilder.ChargedProjectiles with { Value = [ammunition] });
            var restored = crossbow.ToNbt().ItemFromNbt()!;
            var loaded = Assert.Single(restored.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
                DataComponentType.ChargedProjectiles)!.Value!);
            Assert.Same(ItemsRegistry.TippedArrow, loaded.Holder);
            Assert.Equal(
                Potion.StrongPoison,
                loaded.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)!.Potion);
        }

        {
            var item = ItemsRegistry.GetSingleItem(ItemsRegistry.Trident);
            EnchantmentHelper.Enchant(item, EnchantmentsRegistry.Loyalty, 3);
            item[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = 42 };
            var owner = new Zombie { Level = null! };
            var trident = new Trident
            {
                Level = null!,
                Owner = owner,
                PickupItem = item,
                Weapon = new ItemStack(item),
                Pickup = ArrowPickup.Allowed,
                LoyaltyLevel = 3
            };
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

        {
            var effect = Assert.Single(
                ArrowPotionEffects.Get(new PotionContentsDataComponent { Potion = Potion.StrongPoison }));
            Assert.Equal((int)PotionEffect.Poison - 1, effect.Id);
            Assert.Equal(1, effect.Amplifier);
            Assert.Equal(54, effect.Duration / 8);
        }
    }

}

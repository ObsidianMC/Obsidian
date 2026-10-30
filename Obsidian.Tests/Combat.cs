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
using Obsidian.Registries;
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

        // Replacing a component on the same equipped stack must update its modifiers.
        player.Sprinting = false;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, charm);
        player.UpdateEquipmentAttributes();
        charm[DataComponentType.AttributeModifiers] = ComponentBuilder.AttributeModifiers with
        {
            Value = [Modifier(speedId, "test:moving", 0.2, AttributeOperation.Add) with { Slot = AttributeSlot.Hand }]
        };
        player.UpdateEquipmentAttributes();
        Assert.Equal(0.3, player.GetAttributeValue("minecraft:movement_speed"), 5);

        // Move offhand -> mainhand: the later slot's removal must not erase the earlier slot's replacement.
        player.Inventory.SetItem(player.CurrentHeldItemSlot, null);
        player.Inventory.SetItem(45, charm);
        player.UpdateEquipmentAttributes();
        Assert.Equal(0.3, player.GetAttributeValue("minecraft:movement_speed"), 5);
        player.Inventory.SetItem(45, null);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, charm);
        player.UpdateEquipmentAttributes();
        Assert.Equal(0.3, player.GetAttributeValue("minecraft:movement_speed"), 5);
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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task EnderPearlThrowCooldownCollisionTeleportAndNbt()
    {
        await using var context = TestServerFactory.CreateServer($"ender-pearl-{Guid.NewGuid():N}");
        var world = context.World;
        for (var x = -2; x <= 2; x++)
        {
            for (var z = -2; z <= 2; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));
        }

        // Build a wall at z = 5 so a pearl thrown south (+Z) hits it.
        for (var y = 63; y <= 67; y++)
        {
            for (var x = -1; x <= 1; x++)
                await world.SetBlockAsync(new Vector(x, y, 5), BlocksRegistry.Stone);
        }

        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "PearlThrower", new VectorD(0.5, 64, 0.5));
        player.Yaw = 0f;
        player.Pitch = 0f;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));
        var client = (TestClient)player.Client;

        var pearls = new ItemStack(ItemsRegistry.EnderPearl, 3);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, pearls);
        client.ClearPackets();

        // Throwing a pearl spawns EnderPearl, consumes 1 item in Survival, and sends a 20-tick CooldownPacket.
        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(2, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);
        var cooldown = Assert.Single(client.SentPackets.OfType<CooldownPacket>());
        Assert.Equal("minecraft:ender_pearl", cooldown.CooldownGroup);
        Assert.Equal(20, cooldown.CooldownTicks);
        Assert.True(player.IsOnCooldown(pearls));

        var pearl = Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<EnderPearl>());
        Assert.Same(player, pearl.Owner);

        // Attempting to throw again while on cooldown does nothing.
        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(2, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);
        Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<EnderPearl>());

        // Verify NBT roundtrip of EnderPearl preserves Item and Owner UUID.
        var tag = new Obsidian.Nbt.NbtCompound();
        pearl.WriteNbt(tag);
        var restored = new EnderPearl { Level = world };
        restored.ReadNbt(tag);
        Assert.True(restored.Item!.Is(ItemsRegistry.EnderPearl));
        Assert.True(tag.TryGetTag<Obsidian.Nbt.NbtArray<int>>("Owner", out var savedOwner));
        Assert.Equal(EntityNbt.UuidToInts(player.Uuid), savedOwner.GetArray());

        // Mount the player on a vehicle to verify pearl impact dismounts them.
        var pig = new Pig
        {
            Level = world,
            EntityId = Obsidian.Server.GetNextEntityId(),
            Position = player.Position,
            Type = EntityType.Pig
        };
        Assert.True(world.TryAddEntity(pig));
        Assert.True(player.Ride(pig, force: true));
        Assert.Same(pig, player.Vehicle);

        // Tick the pearl until it hits the wall at z = 5.
        for (var tick = 0; tick < 10 && world.GetNonPlayerEntitiesInRange(player.Position, 20).OfType<EnderPearl>().Any(); tick++)
            await pearl.TickAsync();

        // Pearl is removed, player is dismounted, teleported to impact site, and takes 5 fall damage (20 -> 15 HP).
        Assert.Empty(world.GetNonPlayerEntitiesInRange(player.Position, 20).OfType<EnderPearl>());
        Assert.Null(player.Vehicle);
        Assert.True(player.Position.Z > 3.5);
        Assert.Equal(15f, player.Health);
    }

}

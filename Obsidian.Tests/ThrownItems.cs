using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Thrown eggs and snowballs, reached through the vanilla classes that own their <c>Item.use</c> overrides
/// (<c>ItemUseBehaviors</c>, from <c>item_use_classes.json</c>), and the projectiles they throw.
/// </summary>
public sealed class ThrownItems
{
    [Fact(DisplayName = "Throwables and eyes of ender are owned by their vanilla item classes; other items have no throw")]
    public void OwnersAreGenerated()
    {
        Assert.Equal("net.minecraft.world.item.EggItem", ItemUseBehaviors.Owners["minecraft:egg"]);
        Assert.Equal("net.minecraft.world.item.SnowballItem", ItemUseBehaviors.Owners["minecraft:snowball"]);
        Assert.NotNull(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.Egg, 1)));
        Assert.NotNull(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.Snowball, 1)));

        Assert.NotNull(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.EnderPearl, 1)));
        Assert.NotNull(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.EnderEye, 1)));
        Assert.NotNull(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.ExperienceBottle, 1)));

        // Sticks aren't used by throwing at all.
        Assert.Null(ItemUseBehaviors.For(new ItemStack(ItemsRegistry.Stick, 1)));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout, DisplayName = "Throwing an egg spawns an Egg, uses one egg, and the egg is removed on hitting a wall")]
    public async Task EggFliesAndCracks()
    {
        await using var context = TestServerFactory.CreateServer($"egg-{Guid.NewGuid():N}");
        var world = context.World;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));

        // A wall at z = 5 for a southward (+Z) throw to hit.
        for (var y = 63; y <= 67; y++)
            for (var x = -1; x <= 1; x++)
                await world.SetBlockAsync(new Vector(x, y, 5), BlocksRegistry.Stone);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "EggThrower", new VectorD(0.5, 64, 0.5));
        player.Yaw = 0f;
        player.Pitch = 0f;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Egg, 3));

        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(2, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);

        var egg = Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<Egg>());
        Assert.Same(player, egg.Owner);
        Assert.True(egg.Item!.Is(ItemsRegistry.Egg));

        // Thrown eggs keep flying until they hit the wall, then they are removed.
        for (var tick = 0; tick < 40 && world.GetNonPlayerEntitiesInRange(player.Position, 20).OfType<Egg>().Any(); tick++)
            await egg.TickAsync();

        Assert.Empty(world.GetNonPlayerEntitiesInRange(player.Position, 20).OfType<Egg>());
    }

    [Fact(Timeout = TestServerFactory.TestTimeout, DisplayName = "Throwing a splash potion spawns a player-owned splash with the potion's effects and uses one up")]
    public async Task SplashPotionIsThrownWithItsEffects()
    {
        await using var context = TestServerFactory.CreateServer($"splash-{Guid.NewGuid():N}");
        var world = context.World;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "PotionThrower", new VectorD(0.5, 64, 0.5));
        player.Yaw = 0f;
        player.Pitch = 0f;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        var splash = new ItemStack(ItemsRegistry.SplashPotion, 2, new PotionContentsDataComponent { Potion = Potion.Swiftness });
        player.Inventory.SetItem(player.CurrentHeldItemSlot, splash);

        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(1, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);

        var potion = Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<MobProjectile>());
        Assert.Equal(EntityType.SplashPotion, potion.Type);
        Assert.Same(player, potion.Owner);
        Assert.Contains(potion.SplashEffects!, effect => effect.Id == (int)PotionEffect.Speed - 1);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout, DisplayName = "A gliding player launches a firework rocket that boosts them and rides at their hand")]
    public async Task GlidingPlayerLaunchesBoostingRocket()
    {
        await using var context = TestServerFactory.CreateServer($"rocket-{Guid.NewGuid():N}");
        var world = context.World;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Glider", new VectorD(0.5, 64, 0.5));
        player.Yaw = 0f;
        player.Pitch = 0f;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.FireworkRocket, 2));

        // Not gliding: a rocket does nothing and is kept.
        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Empty(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<FireworkRocket>());
        Assert.Equal(2, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);

        player.FlyingWithElytra = true;
        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(1, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);
        var rocket = Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<FireworkRocket>());
        Assert.Same(player, rocket.AttachedTo);

        // Looking along +Z, one tick adds (look * 0.1 + (look * 1.5 - motion) * 0.5) to the player's motion.
        await rocket.TickAsync();
        Assert.Equal(0.85, player.Motion.Z, 6);
        Assert.Equal(player.Motion, rocket.Motion);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout, DisplayName = "Throwing a snowball spawns a player-owned snowball projectile that is consumed in survival")]
    public async Task SnowballFliesAsPlayerProjectile()
    {
        await using var context = TestServerFactory.CreateServer($"snowball-{Guid.NewGuid():N}");
        var world = context.World;
        for (var x = -2; x <= 2; x++)
            for (var z = -2; z <= 2; z++)
                world.LoadRegionByChunk(x, z).SetChunk(new Obsidian.WorldData.Chunk(x, z, world.MinY, world.Height, ChunkGenStage.full));

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "SnowThrower", new VectorD(0.5, 64, 0.5));
        player.Yaw = 0f;
        player.Pitch = 0f;
        Assert.True(world.TryAddPlayer(player));
        Assert.True(world.TryAddEntity(player));

        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Snowball, 2));

        await player.UseItemAsync(InteractionHand.MainHand);
        Assert.Equal(1, player.Inventory.GetItem(player.CurrentHeldItemSlot)!.Count);

        var snowball = Assert.Single(world.GetNonPlayerEntitiesInRange(player.Position, 10).OfType<MobProjectile>());
        Assert.Same(player, snowball.Owner);
        Assert.Equal(EntityType.Snowball, snowball.Type);
        Assert.True(snowball.Motion.Magnitude > 1.4);
    }
}

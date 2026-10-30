using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Entities;

using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class PlayerSaveState
{
    [Theory]
    [InlineData(nameof(Boat))]
    [InlineData(nameof(Pig))]
    public async Task RiderComesBackOnTheirVehicle(string kind)
    {
        await using var context = TestServerFactory.CreateServer($"player-rider-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Rider");
        await player.LoadAsync(loadFromPersistentWorld: false);
        var position = new VectorD(4.5, 64, 4.5);
        Entity vehicle = kind == nameof(Boat)
            ? new Boat { Level = context.World, Position = position }
            : new Pig { Level = context.World, Position = position };
        context.World.SpawnEntity(vehicle);
        Assert.True(player.StartRiding(vehicle));

        var returning = await LeaveAndReturnAsync(context, player);
        var restored = returning.Vehicle!;
        Assert.IsType(vehicle.GetType(), restored);
        Assert.Equal(vehicle.Uuid, restored.Uuid);
        Assert.Same(returning, Assert.Single(restored.Passengers));
    }

    [Fact]
    public async Task EverySavedPlayerFieldSurvivesALeaveAndJoin()
    {
        await using var context = TestServerFactory.CreateServer($"player-fields-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Saver");
        await player.LoadAsync(loadFromPersistentWorld: false);

        player.Position = new VectorD(12.5, 70, -3.5);
        player.Health = 14;
        player.FallDistance = 3.5f;
        player.Air = 123;
        player.HurtTime = 5;
        player.DeathTime = 9;
        player.SleepTimer = 12;
        player.AbsorbtionAmount = 4;
        player.GameMode = GameMode.Creative;
        player.FoodLevel = 13;
        player.FoodTickTimer = 17;
        player.FoodSaturationLevel = 2.5f;
        player.FoodExhaustionLevel = 1.25f;
        player.XpLevel = 7;
        player.XpTotal = 150;
        player.XpP = 0.5f;
        player.CurrentHeldItemSlot = 4;
        player.SeenCredits = true;
        player.RespawnPoint = new RespawnPoint("minecraft:overworld", new Vector(10, 70, -4), 90f, true);
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 3));
        player.Inventory.SetItem(20, new ItemStack(ItemsRegistry.Stone, 9));
        player.Inventory.SetItem(5, new ItemStack(ItemsRegistry.Stone, 1));
        player.EnderInventory.SetItem(3, new ItemStack(ItemsRegistry.Diamond, 2));
        var enchantmentSeed = player.EnchantmentSeed;

        var returning = await LeaveAndReturnAsync(context, player);

        Assert.Equal(new VectorD(12.5, 70, -3.5), returning.Position);
        Assert.Equal(14, returning.Health);
        Assert.Equal(3.5f, returning.FallDistance);
        Assert.Equal(123, returning.Air);
        Assert.Equal(5, returning.HurtTime);
        Assert.Equal(9, returning.DeathTime);
        Assert.Equal(12, returning.SleepTimer);
        Assert.Equal(4, returning.AbsorbtionAmount);
        Assert.Equal(GameMode.Creative, returning.GameMode);
        Assert.Equal(13, returning.FoodLevel);
        Assert.Equal(17, returning.FoodTickTimer);
        Assert.Equal(2.5f, returning.FoodSaturationLevel);
        Assert.Equal(1.25f, returning.FoodExhaustionLevel);
        Assert.Equal(7, returning.XpLevel);
        Assert.Equal(150, returning.XpTotal);
        Assert.Equal(0.5f, returning.XpP);
        Assert.Equal(enchantmentSeed, returning.EnchantmentSeed);
        Assert.Equal(player.CurrentHeldItemSlot, returning.CurrentHeldItemSlot);
        Assert.True(returning.SeenCredits);
        Assert.Equal(player.RespawnPoint, returning.RespawnPoint);
        Assert.Equal(3, returning.Inventory.GetItem(36)?.Count);
        Assert.Equal(9, returning.Inventory.GetItem(20)?.Count);
        Assert.Equal(1, returning.Inventory.GetItem(5)?.Count);
        Assert.Equal(2, returning.EnderInventory.GetItem(3)?.Count);

        // The join's player-info step sends the saved hotbar slot the client starts from.
        await returning.SendPlayerInfoAsync();
        var held = Assert.Single(Assert.IsType<TestClient>(returning.Client).SentPackets.OfType<SetHeldSlotPacket>());
        Assert.Equal(4, held.Slot);
    }

    // Saves the player the way leaving does, takes their vehicle out of the world, then loads them as a joining player.
    private static async Task<Obsidian.Entities.Player> LeaveAndReturnAsync(RealServerContext context, Obsidian.Entities.Player player)
    {
        await player.SaveAsync();
        if (player.RootVehicle is { Passengers: [var rider] } root && ReferenceEquals(rider, player))
        {
            player.StopRiding();
            await context.World.DestroyEntityAsync(root);
        }

        var returning = new Obsidian.Entities.Player(player.Uuid, player.Username, new TestClient(context.Server), context.World) { Server = context.Server };
        await returning.LoadAsync(loadFromPersistentWorld: false);
        returning.RestoreSavedVehicle();
        return returning;
    }
}

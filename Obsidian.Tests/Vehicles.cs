using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Smoke tests of boats and minecarts: players ride both, and each kind keeps its fields through saving.
/// </summary>
public sealed class Vehicles
{
    private const int Floor = 69;

    [Fact(DisplayName = "Players get in boats and minecarts, and out by sneaking", Timeout = TestServerFactory.TestTimeout)]
    public async Task PlayersRideVehicles()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        var player = await WorldBlockEntities.JoinAsync(context);
        await StoneAsync(world, 6, 10, 6, 10);

        var boat = Spawn(world, new Boat { Level = world }, new VectorD(8.5, Floor + 1, 8.5));
        Assert.True(await boat.InteractAsync(player, InteractionHand.MainHand));
        Assert.Same(boat, player.Vehicle);

        // A second player, or a mob, takes the back seat.
        Assert.Single(boat.Passengers);

        player.Sneaking = true;
        await world.DoWorldTickAsync();
        Assert.Null(player.Vehicle);

        player.Sneaking = false;
        var cart = Spawn(world, new Minecart { Level = world }, new VectorD(9.5, Floor + 1, 9.5));
        Assert.True(await cart.InteractAsync(player, InteractionHand.MainHand));
        Assert.Same(cart, player.Vehicle);
    }

    [Fact(DisplayName = "Vehicles keep their kind's fields through saving")]
    public void VehiclesSave()
    {
        var chest = new ChestMinecart { Level = null!, LootTable = "minecraft:chests/abandoned_mineshaft", LootTableSeed = 7 };
        var tag = new NbtCompound();
        chest.WriteNbt(tag);
        var loadedChest = new ChestMinecart { Level = null! };
        loadedChest.ReadNbt(tag);
        Assert.Equal("minecraft:chests/abandoned_mineshaft", loadedChest.LootTable);
        Assert.Equal(7, loadedChest.LootTableSeed);
        Assert.False(tag.HasTag("DisplayOffset"));

        var furnace = new FurnaceMinecart { Level = null!, Fuel = 1234, Push = new VectorD(0.5, 0, -1) };
        tag = new NbtCompound();
        furnace.WriteNbt(tag);
        var loadedFurnace = new FurnaceMinecart { Level = null! };
        loadedFurnace.ReadNbt(tag);
        Assert.Equal(1234, loadedFurnace.Fuel);
        Assert.Equal(new VectorD(0.5, 0, -1), loadedFurnace.Push);

        var tnt = new TntMinecart { Level = null!, Fuse = 12, ExplosionPower = 6 };
        tag = new NbtCompound();
        tnt.WriteNbt(tag);
        var loadedTnt = new TntMinecart { Level = null! };
        loadedTnt.ReadNbt(tag);
        Assert.Equal(12, loadedTnt.Fuse);
        Assert.Equal(6, loadedTnt.ExplosionPower);
        Assert.False(tag.HasTag("explosion_speed_factor"));

        var boat = new ChestBoat { Level = null!, Type = EntityType.CherryChestBoat };
        boat.Inventory.SetItem(26, new ItemStack(ItemsRegistry.Apple, 5));
        tag = new NbtCompound();
        boat.WriteNbt(tag);
        var loadedBoat = new ChestBoat { Level = null! };
        loadedBoat.ReadNbt(tag);
        Assert.Equal(5, loadedBoat.Inventory[26]?.Count);
    }

    private static async Task<RealServerContext> CreateAsync()
    {
        var context = TestServerFactory.CreateServer($"vehicles-{Guid.NewGuid():N}");
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                WorldBlockEntities.AddEmptyChunk(context.World, chunkX, chunkZ);
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await context.World.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }

        return context;
    }

    private static async Task StoneAsync(World world, int minX, int maxX, int minZ, int maxZ)
    {
        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
                await world.SetBlockAsync(new Vector(x, Floor, z), BlocksRegistry.Stone);
        }
    }

    private static T Spawn<T>(World world, T vehicle, VectorD position) where T : VehicleEntity
    {
        vehicle.EntityId = Server.GetNextEntityId();
        vehicle.Position = position;
        vehicle.LastPosition = position;
        Assert.True(world.TrySpawnEntity(vehicle, hostOwned: true));
        return vehicle;
    }
}

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
/// Smoke tests of boats and minecarts: minecarts follow rails, boats float, players ride both, hopper minecarts take in
/// items, and each kind keeps its fields through saving.
/// </summary>
public sealed class Vehicles
{
    private const int Floor = 69;
    private const int Track = 70;

    [Fact(DisplayName = "A minecart follows the rails, slowing as it rolls", Timeout = TestServerFactory.TestTimeout)]
    public async Task MinecartsFollowRails()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 0, 15, 5, 5);
        for (var x = 0; x <= 15; x++)
            await world.SetBlockAsync(new Vector(x, Track, 5), StraightRail(BlocksRegistry.Rail));

        var cart = Spawn(world, new Minecart { Level = world }, new VectorD(2.5, Track + 0.0625, 5.4));
        cart.Motion = new VectorD(0.3, 0, 0);

        var lastX = cart.Position.X;
        var lastSpeed = double.MaxValue;
        for (var tick = 0; tick < 20; tick++)
        {
            await world.DoWorldTickAsync();

            Assert.True(cart.OnRails);
            Assert.Equal(Track + 0.0625, cart.Position.Y, 6);
            Assert.Equal(5.5, cart.Position.Z, 6);
            Assert.True(cart.Position.X > lastX, $"The cart stopped at {cart.Position}.");
            Assert.True(Math.Abs(cart.Motion.X) <= lastSpeed + 1.0E-9);

            lastX = cart.Position.X;
            lastSpeed = Math.Abs(cart.Motion.X);
        }
    }

    [Fact(DisplayName = "A boat dropped into still water floats at its surface", Timeout = TestServerFactory.TestTimeout)]
    public async Task BoatsFloat()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await PoolAsync(world);

        var boat = Spawn(world, new Boat { Level = world }, new VectorD(5.5, 68, 5.5));
        for (var tick = 0; tick < 200; tick++)
            await world.DoWorldTickAsync();

        // Vanilla's equilibrium: buoyancy (0.65 of the boat under the surface at 66 + 8/9) balances gravity.
        Assert.InRange(boat.Position.Y, 66.35, 66.75);
        Assert.Equal(5.5, boat.Position.X, 3);
        Assert.Equal(5.5, boat.Position.Z, 3);
        Assert.True(Math.Abs(boat.Motion.Y) < 0.02, $"Still bobbing at {boat.Motion}.");
    }

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

    [Fact(DisplayName = "A hopper minecart takes in an item that was already lying still",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task HopperMinecartsTakeInRestingItems()
    {
        await using var context = await CreateAsync();
        var world = context.World;

        // Items only tick near a player, as in vanilla.
        await WorldBlockEntities.JoinAsync(context);
        await StoneAsync(world, 4, 6, 5, 5);
        await world.SetBlockAsync(new Vector(5, Track, 5), StraightRail(BlocksRegistry.Rail));

        // On the floor from the start, like an item loaded from a save, so it never moves.
        world.TrySpawnEntity(new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(5.5, Track, 5.5),
            MovementFlags = MovementFlags.OnGround,
            Item = new ItemStack(ItemsRegistry.Diamond, 3)
        });
        var cart = Spawn(world, new HopperMinecart { Level = world }, new VectorD(5.5, Track + 0.0625, 5.5));

        for (var tick = 0; tick < 5 && cart.Inventory[0] is null; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(3, cart.Inventory[0]?.Count);
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

    // Still water from 62 to 66, five blocks across, walled in.
    private static async Task PoolAsync(World world)
    {
        for (var x = 2; x <= 8; x++)
        {
            for (var z = 2; z <= 8; z++)
            {
                await world.SetBlockAsync(new Vector(x, 61, z), BlocksRegistry.Stone);
                for (var y = 62; y <= 66; y++)
                {
                    var wall = x is 2 or 8 || z is 2 or 8;
                    await world.SetBlockAsync(new Vector(x, y, z), wall ? BlocksRegistry.Stone : BlocksRegistry.Water);
                }
            }
        }
    }

    private static IBlock StraightRail(IBlock rail) => rail.WithProperty("shape", "east_west");

    private static T Spawn<T>(World world, T vehicle, VectorD position) where T : VehicleEntity
    {
        vehicle.EntityId = Server.GetNextEntityId();
        vehicle.Position = position;
        vehicle.LastPosition = position;
        Assert.True(world.TrySpawnEntity(vehicle, hostOwned: true));
        return vehicle;
    }
}

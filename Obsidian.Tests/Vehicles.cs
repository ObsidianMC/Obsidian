using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Boats and minecarts move, break and carry like vanilla's: rails join up and carry power, minecarts follow them,
/// boats float, and each kind of minecart does its job.
/// </summary>
public sealed class Vehicles
{
    private const int Floor = 69;
    private const int Track = 70;

    [Fact(DisplayName = "Rails turn to join the rails placed beside them", Timeout = TestServerFactory.TestTimeout)]
    public async Task RailsJoinIntoCurves()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 3, 7, 3, 7);

        await world.SetBlockAsync(new Vector(5, Track, 5), BlocksRegistry.Rail, true);
        await world.SetBlockAsync(new Vector(6, Track, 5), BlocksRegistry.Rail, true);
        Assert.Equal("east_west", await ShapeAsync(world, 5, 5));

        await world.SetBlockAsync(new Vector(5, Track, 6), BlocksRegistry.Rail, true);

        Assert.Equal("south_east", await ShapeAsync(world, 5, 5));
        Assert.Equal("east_west", await ShapeAsync(world, 6, 5));
        Assert.Equal("north_south", await ShapeAsync(world, 5, 6));
    }

    [Fact(DisplayName = "Powered rails carry power eight rails along", Timeout = TestServerFactory.TestTimeout)]
    public async Task PoweredRailsCarryPower()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 0, 14, 4, 4);

        for (var x = 1; x <= 12; x++)
            await world.SetBlockAsync(new Vector(x, Track, 4), StraightRail(BlocksRegistry.PoweredRail));

        await world.SetBlockAsync(new Vector(0, Track, 4), BlocksRegistry.RedstoneBlock, true);

        for (var x = 1; x <= 12; x++)
        {
            var rail = await world.GetBlockAsync(new Vector(x, Track, 4));
            Assert.Equal(x <= 9 ? "true" : "false", rail!.GetProperty("powered"));
        }
    }

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
            Assert.Equal(Track, cart.Position.Y, 6);
            Assert.Equal(5.5, cart.Position.Z, 6);
            Assert.True(cart.Position.X > lastX, $"The cart stopped at {cart.Position}.");
            Assert.True(Math.Abs(cart.Motion.X) <= lastSpeed + 1.0E-9);

            lastX = cart.Position.X;
            lastSpeed = Math.Abs(cart.Motion.X);
        }
    }

    [Fact(DisplayName = "Powered rails speed minecarts up and unpowered ones stop them",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PoweredRailsBoostAndBrake()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 0, 15, 5, 6);
        for (var x = 0; x <= 15; x++)
        {
            var kind = x is >= 4 and <= 6 ? StraightRail(BlocksRegistry.PoweredRail).WithProperty("powered", "true")
                : StraightRail(BlocksRegistry.Rail);
            await world.SetBlockAsync(new Vector(x, Track, 5), kind);
            await world.SetBlockAsync(new Vector(x, Track, 6), x == 4
                ? StraightRail(BlocksRegistry.PoweredRail)
                : StraightRail(BlocksRegistry.Rail));
        }

        var boosted = Spawn(world, new Minecart { Level = world }, new VectorD(1.5, Track + 0.0625, 5.5));
        boosted.Motion = new VectorD(0.1, 0, 0);
        var braked = Spawn(world, new Minecart { Level = world }, new VectorD(1.5, Track + 0.0625, 6.5));
        braked.Motion = new VectorD(0.1, 0, 0);

        for (var tick = 0; tick < 40; tick++)
            await world.DoWorldTickAsync();

        Assert.True(boosted.Position.X > 10, $"The boosted cart only reached {boosted.Position}.");
        Assert.InRange(braked.Position.X, 4, 5);
        Assert.Equal(VectorD.Zero, braked.Motion);
    }

    [Fact(DisplayName = "A minecart on a detector rail powers it until it leaves", Timeout = TestServerFactory.TestTimeout)]
    public async Task DetectorRailsSenseMinecarts()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 4, 6, 5, 5);
        var detector = new Vector(5, Track, 5);
        await world.SetBlockAsync(detector, StraightRail(BlocksRegistry.DetectorRail));

        var cart = Spawn(world, new ChestMinecart { Level = world }, new VectorD(5.5, Track + 0.0625, 5.5));
        cart.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
        await world.DoWorldTickAsync();

        Assert.Equal("true", (await world.GetBlockAsync(detector))!.GetProperty("powered"));
        Assert.Equal(15, world.GetDirectSignal(detector, BlockFace.Up));
        lock (world.Fluids.TickLock)
            Assert.Equal(1, world.BlockUpdates.AnalogOutputOf((await world.GetBlockAsync(detector))!, detector, BlockFace.North));

        await cart.DiscardAsync();
        for (var tick = 0; tick < 21; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal("false", (await world.GetBlockAsync(detector))!.GetProperty("powered"));
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

    [Fact(DisplayName = "Hits add up until a boat breaks into its item", Timeout = TestServerFactory.TestTimeout)]
    public async Task BoatsBreakIntoTheirItem()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 3, 7, 3, 7);

        var boat = Spawn(world, Boat.Create(EntityType.BirchBoat, world), new VectorD(5.5, Floor + 1, 5.5));
        await boat.HurtAsync(null, 3, VehicleDamage.Attack);
        Assert.False(boat.IsRemoved);
        Assert.Equal(30, boat.Damage);
        Assert.Equal(10, boat.HurtTime);

        await world.DoWorldTickAsync();
        Assert.Equal(29, boat.Damage);

        await boat.HurtAsync(null, 2, VehicleDamage.Attack);
        Assert.True(boat.IsRemoved);

        var drop = world.GetEntitiesInRange(boat.Position, 2).OfType<ItemEntity>().Single();
        Assert.True(drop.Item!.Is(ItemsRegistry.BirchBoat));
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

    [Fact(DisplayName = "A TNT minecart lit by an activator rail explodes 80 ticks later",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task TntMinecartsExplode()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 3, 7, 5, 5);
        await world.SetBlockAsync(new Vector(5, Track, 5),
            StraightRail(BlocksRegistry.ActivatorRail).WithProperty("powered", "true"));

        var cart = Spawn(world, new TntMinecart { Level = world }, new VectorD(5.5, Track + 0.0625, 5.5));
        await world.DoWorldTickAsync();
        Assert.Equal(79, cart.Fuse);

        for (var tick = 0; tick < 79; tick++)
            await world.DoWorldTickAsync();

        Assert.True(cart.IsRemoved);

        // A lit cart spares the rail it's on.
        Assert.Equal(BlocksRegistry.ActivatorRail.RegistryId, (await world.GetBlockAsync(new Vector(5, Track, 5)))!.RegistryId);
    }

    [Fact(DisplayName = "A hopper minecart takes in the items above it", Timeout = TestServerFactory.TestTimeout)]
    public async Task HopperMinecartsTakeInItems()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        await StoneAsync(world, 4, 6, 5, 5);
        await world.SetBlockAsync(new Vector(5, Track, 5), StraightRail(BlocksRegistry.Rail));

        var cart = Spawn(world, new HopperMinecart { Level = world }, new VectorD(5.5, Track + 0.0625, 5.5));
        world.TrySpawnEntity(new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(5.5, Track + 1, 5.5),
            Item = new ItemStack(ItemsRegistry.Diamond, 3)
        });

        for (var tick = 0; tick < 5 && cart.Inventory[0] is null; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(3, cart.Inventory[0]?.Count);
    }

    [Fact(DisplayName = "A furnace minecart burns its fuel to push itself", Timeout = TestServerFactory.TestTimeout)]
    public async Task FurnaceMinecartsPushThemselves()
    {
        await using var context = await CreateAsync();
        var world = context.World;
        var player = await WorldBlockEntities.JoinAsync(context);
        await StoneAsync(world, 0, 15, 7, 7);
        for (var x = 0; x <= 15; x++)
            await world.SetBlockAsync(new Vector(x, Track, 7), StraightRail(BlocksRegistry.Rail));

        var cart = Spawn(world, new FurnaceMinecart { Level = world }, new VectorD(9.5, Track + 0.0625, 7.5));
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Coal, 2));

        // The player stands west of it, so it goes east.
        player.Position = new VectorD(7.5, Track, 7.5);
        Assert.True(await cart.InteractAsync(player, InteractionHand.MainHand));
        Assert.Equal(3600, cart.Fuel);

        for (var tick = 0; tick < 20; tick++)
            await world.DoWorldTickAsync();

        Assert.True(cart.Position.X > 10, $"The cart only reached {cart.Position}.");
        Assert.Equal(3580, cart.Fuel);
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

    private static async Task<string?> ShapeAsync(World world, int x, int z) =>
        (await world.GetBlockAsync(new Vector(x, Track, z)))?.GetProperty("shape");

    private static T Spawn<T>(World world, T vehicle, VectorD position) where T : VehicleEntity
    {
        vehicle.EntityId = Server.GetNextEntityId();
        vehicle.Position = position;
        vehicle.LastPosition = position;
        Assert.True(world.TrySpawnEntity(vehicle, hostOwned: true));
        return vehicle;
    }
}

using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class RedstoneRailTests
{
    // A stone floor under the rails, at y 63.
    private static readonly IBlock Stone = BlocksRegistry.Get("minecraft:stone");

    private static IBlock Rail(string shape, int id = BlockIds.Rail) =>
        BlocksRegistry.GetDefault(id).WithProperty("shape", shape).WithProperty("waterlogged", "false");

    private static async Task<RedstoneTestWorld> FloorAsync(int x0, int x1)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        for (var x = x0; x <= x1; x++)
            await world.Place(new Vector(x, 63, 8), Stone);

        return world;
    }

    [Fact]
    public async Task APlacedRailTurnsToJoinTheRailsEitherSide()
    {
        var world = await FloorAsync(6, 10);
        await world.Place(new Vector(7, 64, 8), Rail("east_west"));
        await world.Place(new Vector(9, 64, 8), Rail("east_west"));

        // Placed north-south, between two east-west rails, it joins them.
        await world.Place(new Vector(8, 64, 8), Rail("north_south"));

        Assert.Equal("east_west", world.GetLoadedBlock(new Vector(8, 64, 8))!.GetProperty("shape"));
    }

    [Fact]
    public async Task APoweredRailIsPoweredByARedstoneBlockBesideIt()
    {
        var world = await FloorAsync(6, 10);
        var rail = new Vector(8, 64, 8);
        await world.Place(rail, Rail("north_south", BlockIds.PoweredRail).WithProperty("powered", "false"));

        await world.Place(new Vector(8, 64, 9), BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal("true", world.GetLoadedBlock(rail)!.GetProperty("powered"));

        await world.Place(new Vector(8, 64, 9), BlocksRegistry.Air);
        Assert.Equal("false", world.GetLoadedBlock(rail)!.GetProperty("powered"));
    }

    [Fact]
    public async Task ARailLosingItsSupportDrops()
    {
        var world = await FloorAsync(6, 10);
        var rail = new Vector(8, 64, 8);
        await world.Place(rail, Rail("north_south"));

        await world.Place(new Vector(8, 63, 8), BlocksRegistry.Air);

        Assert.True(world.GetLoadedBlock(rail)!.IsAir);
    }

    [Fact]
    public async Task ADetectorRailWithoutAMinecartIsNotPowered()
    {
        var world = await FloorAsync(6, 10);
        var rail = new Vector(8, 64, 8);
        await world.Place(rail, Rail("north_south", BlockIds.DetectorRail).WithProperty("powered", "false"));

        Assert.Equal("false", world.GetLoadedBlock(rail)!.GetProperty("powered"));
        Assert.Equal(0, RedstoneDetectorRail.AnalogOutput(world.GetLoadedBlock(rail)!, []));
    }
}

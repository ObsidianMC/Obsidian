using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Redstone torch inversion through a wire (R4, see docs/redstone/02-core-circuits.md). Expectations are from the 1.21.11 vanilla oracle:
/// a wire beside a stone support powers it, so the torch on it goes off; removing and restoring the wire's source turns the torch on
/// and off again.
/// </summary>
public class RedstoneTorchTests
{
    // A redstone block feeds a wire along y=99 (stone floor at y=98). The support is stone beside the wire's east end.
    private static async Task<RedstoneTestWorld> WireWithSource()
    {
        var world = new RedstoneTestWorld();
        world.Load(-4, 6, 97, 102, -2, 2, ringChunks: 1);
        for (var x = -4; x <= 6; x++)
            for (var z = -2; z <= 2; z++)
                await world.Place(new Vector(x, 98, z), BlocksRegistry.Get("minecraft:stone"));
        var source = new Vector(-2, 99, 0);
        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        for (var x = -1; x <= 3; x++)
            await world.Place(new Vector(x, 99, 0), BlocksRegistry.Get("minecraft:redstone_wire"));
        return world;
    }

    private static string Lit(RedstoneTestWorld world) => world.GetLoadedBlock(new Vector(4, 100, 0))!.GetProperty("lit");

    [Fact]
    public async Task TorchOnASupportFedByAWireGoesOffAndRelightsWhenTheWireLosesPower()
    {
        var world = await WireWithSource();
        await world.Place(new Vector(4, 99, 0), BlocksRegistry.Get("minecraft:stone"));
        await world.Place(new Vector(4, 100, 0), BlocksRegistry.Get("minecraft:redstone_torch"));
        for (var tick = 0; tick < 8; tick++)
            await world.Advance();
        Assert.Equal("false", Lit(world));

        await world.Place(new Vector(-2, 99, 0), BlocksRegistry.Air);
        for (var tick = 0; tick < 8; tick++)
            await world.Advance();
        Assert.Equal("true", Lit(world));

        await world.Place(new Vector(-2, 99, 0), BlocksRegistry.Get("minecraft:redstone_block"));
        for (var tick = 0; tick < 8; tick++)
            await world.Advance();
        Assert.Equal("false", Lit(world));
    }

    [Fact]
    public async Task WireUnderTheSupportDoesNotTurnTheTorchOff()
    {
        var world = await WireWithSource();
        await world.Place(new Vector(0, 100, 0), BlocksRegistry.Get("minecraft:stone"));
        await world.Place(new Vector(0, 101, 0), BlocksRegistry.Get("minecraft:redstone_torch"));
        for (var tick = 0; tick < 8; tick++)
            await world.Advance();
        Assert.Equal("true", world.GetLoadedBlock(new Vector(0, 101, 0))!.GetProperty("lit"));
    }
}

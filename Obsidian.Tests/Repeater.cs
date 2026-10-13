using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class RepeaterTests
{
    [Fact]
    public async Task PoweredRepeaterOutputsOnlyForward()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var repeater = new Vector(8, 64, 8);
        await world.Place(repeater, BlocksRegistry.Get(RedstoneRepeater.Name)
            .WithProperty("facing", "west")
            .WithProperty("delay", "1")
            .WithProperty("locked", "false")
            .WithProperty("powered", "true"));

        Assert.Equal(15, world.Engine.GetSignal(repeater, BlockFace.West));
        Assert.Equal(15, world.Engine.GetDirectSignal(repeater, BlockFace.West));
        Assert.Equal(0, world.Engine.GetSignal(repeater, BlockFace.East));
        Assert.Equal(0, world.Engine.GetDirectSignal(repeater, BlockFace.East));
    }

    [Fact]
    public async Task UsingARepeaterCyclesItsDelay()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var repeater = new Vector(8, 64, 8);
        await world.Place(repeater, BlocksRegistry.Get(RedstoneRepeater.Name)
            .WithProperty("facing", "west")
            .WithProperty("delay", "1")
            .WithProperty("locked", "false")
            .WithProperty("powered", "false"));

        for (var expected = 2; expected <= 4; expected++)
        {
            await RedstoneRepeater.CycleDelayAsync(world, repeater, world.GetLoadedBlock(repeater)!);
            Assert.Equal(expected.ToString(), world.GetLoadedBlock(repeater)!.GetProperty("delay"));
        }

        await RedstoneRepeater.CycleDelayAsync(world, repeater, world.GetLoadedBlock(repeater)!);
        Assert.Equal("1", world.GetLoadedBlock(repeater)!.GetProperty("delay"));
    }
}

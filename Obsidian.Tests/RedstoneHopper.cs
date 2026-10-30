using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

public class RedstoneHopperTests
{
    private static readonly Vector Hopper = new(8, 64, 8);
    private static readonly Vector Source = new(7, 64, 8);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PoweredHopperIsDisabledUntilThePowerLeaves(bool sourceFirst)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        if (sourceFirst)
            await world.Place(Source, BlocksRegistry.Get("minecraft:redstone_block"));

        await world.Place(Hopper, BlocksRegistry.GetDefault(BlockIds.Hopper).WithProperty("enabled", "true"));
        if (!sourceFirst)
        {
            Assert.Equal("true", world.GetLoadedBlock(Hopper)!.GetProperty("enabled"));
            await world.Place(Source, BlocksRegistry.Get("minecraft:redstone_block"));
        }
        Assert.Equal("false", world.GetLoadedBlock(Hopper)!.GetProperty("enabled"));

        await world.Place(Source, BlocksRegistry.Air);
        Assert.Equal("true", world.GetLoadedBlock(Hopper)!.GetProperty("enabled"));
    }
}

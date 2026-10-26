using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class RedstoneLecternTests
{
    private static readonly Vector Lectern = new(8, 64, 8);

    private static IBlock LecternState(string powered) =>
        BlocksRegistry.GetDefault(BlockIds.Lectern).WithProperty("powered", powered);

    [Fact]
    public async Task PoweredLecternGivesFullSignalUpAndWeakerToTheSides()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(Lectern, LecternState("true"));

        Assert.Equal(15, world.Engine.GetDirectSignal(Lectern, BlockFace.Up));
        Assert.Equal(0, world.Engine.GetDirectSignal(Lectern, BlockFace.West));
        Assert.Equal(15, world.Engine.GetSignal(Lectern, BlockFace.West));
    }

    [Fact]
    public async Task PageTurnPowersTheLecternForTwoTicksThenReleasesIt()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        await world.Place(Lectern, LecternState("false"));

        await RedstoneLectern.TurnPageAsync(world, Lectern);
        Assert.Equal("true", world.GetLoadedBlock(Lectern)!.GetProperty("powered"));

        await world.Advance();
        Assert.Equal("true", world.GetLoadedBlock(Lectern)!.GetProperty("powered"));

        await world.Advance();
        Assert.Equal("false", world.GetLoadedBlock(Lectern)!.GetProperty("powered"));
    }
}

using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The daylight detector (R4, see docs/redstone/02-core-circuits.md). The cycle samples come from the 1.21.11 vanilla oracle
/// (open sky, stone floor, <c>time set</c> then 40 stepped ticks). The sample-to-game-time offset is not pinned yet: 34 to 39 ticks all
/// reproduce every reading, so these tests use 36. The replay scenario will fix the exact phase.
/// </summary>
public class DaylightDetectorTests
{
    private const int SampleOffset = 36;

    // (sampled time of day, power when not inverted, power when inverted), read from the vanilla oracle.
    public static TheoryData<int, int, int> OracleReadings => new()
    {
        { 0, 7, 0 }, { 100, 7, 0 }, { 300, 8, 0 }, { 500, 8, 0 }, { 1000, 10, 0 }, { 2000, 12, 0 }, { 3000, 13, 0 },
        { 4500, 15, 0 }, { 6000, 15, 0 }, { 7500, 15, 0 }, { 9000, 13, 0 }, { 10500, 11, 0 }, { 11500, 8, 0 },
        { 12000, 6, 1 }, { 12200, 5, 2 }, { 12500, 4, 4 }, { 12800, 3, 5 }, { 13000, 2, 7 }, { 13300, 1, 8 },
        { 13700, 0, 11 }, { 13900, 0, 11 }, { 14000, 0, 11 }, { 14500, 0, 11 }, { 16000, 0, 11 }, { 18000, 0, 11 },
        { 19500, 0, 11 }, { 20500, 0, 11 }, { 21500, 0, 11 }, { 22000, 0, 11 }, { 22500, 1, 9 }, { 23000, 2, 6 },
        { 23300, 4, 4 }, { 23600, 5, 3 },
    };

    [Theory]
    [MemberData(nameof(OracleReadings))]
    public void PowerMatchesTheVanillaOracleAcrossTheDay(int sample, int normal, int inverted)
    {
        Assert.Equal(normal, DaylightDetector.Power(15, sample + SampleOffset, inverted: false));
        Assert.Equal(inverted, DaylightDetector.Power(15, sample + SampleOffset, inverted: true));
    }

    [Fact]
    public async Task ItGivesWeakPowerToEveryNeighbourAndNoDirectPower()
    {
        var world = new RedstoneTestWorld { DayTime = 6000 };
        world.Load(0, 15, 62, 68, 0, 15);
        var detector = new Vector(8, 64, 8);
        await world.Place(detector, BlocksRegistry.GetDefault(DaylightDetector.BlockId));
        await DaylightDetector.RefreshAsync(world, detector);
        Assert.Equal("15", world.GetLoadedBlock(detector)!.GetProperty("power"));

        foreach (var face in new[] { BlockFace.Up, BlockFace.Down, BlockFace.North, BlockFace.South, BlockFace.East, BlockFace.West })
            Assert.True(world.Engine.HasSignal(detector.Offset(face), face.Opposite()), $"no weak signal toward {face}");

        // A conductor beside the detector takes no direct power from it.
        var stone = detector.Offset(BlockFace.Up);
        await world.Place(stone, BlocksRegistry.Get("minecraft:stone"));
        Assert.Equal(0, world.Engine.GetDirectSignalTo(stone));
    }

    [Fact]
    public async Task RefreshWritesOnlyChangesAndSchedulesTheNextTwentyTickBoundary()
    {
        var world = new RedstoneTestWorld { DayTime = 6000 };
        world.Load(0, 15, 62, 68, 0, 15, ringChunks: 1);
        var detector = new Vector(8, 64, 8);
        await world.Place(detector, BlocksRegistry.GetDefault(DaylightDetector.BlockId));

        for (var tick = 0; tick < 45; tick++)
            await world.Advance();
        world.ScheduledBlockTicks.Clear();
        await DaylightDetector.RefreshAsync(world, detector);

        Assert.Equal("15", world.GetLoadedBlock(detector)!.GetProperty("power"));
        // Game time 45 is 5 ticks past a boundary, so the next update is 15 ticks away.
        Assert.Equal((detector, BlocksRegistry.GetDefault(DaylightDetector.BlockId).UnlocalizedName, 15), world.ScheduledBlockTicks[^1]);

        for (var tick = 0; tick < 15; tick++)
            await world.Advance();
        await DaylightDetector.RefreshAsync(world, detector);
        Assert.Equal(20, world.ScheduledBlockTicks[^1].Delay);
    }

    [Fact]
    public async Task RightClickInvertsTheDetectorAndRecomputesItsPower()
    {
        var world = new RedstoneTestWorld { DayTime = 18000 };
        world.Load(0, 15, 62, 68, 0, 15);
        var detector = new Vector(8, 64, 8);
        await world.Place(detector, BlocksRegistry.GetDefault(DaylightDetector.BlockId));

        // Midnight: the plain detector reads 0, so the inverted one reads its full darkness of 11.
        await DaylightDetector.ToggleAsync(world, detector, world.GetLoadedBlock(detector)!);
        Assert.Equal("true", world.GetLoadedBlock(detector)!.GetProperty("inverted"));
        Assert.Equal("11", world.GetLoadedBlock(detector)!.GetProperty("power"));

        await DaylightDetector.ToggleAsync(world, detector, world.GetLoadedBlock(detector)!);
        Assert.Equal("false", world.GetLoadedBlock(detector)!.GetProperty("inverted"));
        Assert.Equal("0", world.GetLoadedBlock(detector)!.GetProperty("power"));
    }
}

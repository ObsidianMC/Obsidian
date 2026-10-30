using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The daylight detector. Its power across the day is checked against readings recorded from a headless vanilla server
/// (<c>Assets/daylight_detector.json</c>, by the asset generator's scenario captures): each holds the clock's time in
/// the tick a detector and an inverted one updated, and their power then.
/// </summary>
public class DaylightDetectorTests
{
    [Fact]
    public void PowerMatchesVanillaAcrossTheDay()
    {
        using var stream = typeof(DaylightDetectorTests).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.daylight_detector.json")!;
        using var fixture = JsonDocument.Parse(stream);
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.RootElement.GetProperty("version").GetString());

        var readings = fixture.RootElement.GetProperty("readings").EnumerateArray().ToArray();
        Assert.NotEmpty(readings);

        var mismatches = new List<string>();
        foreach (var reading in readings)
        {
            var dayTime = reading.GetProperty("dayTime").GetInt64();
            var power = DaylightDetector.Power(15, dayTime, inverted: false);
            var invertedPower = DaylightDetector.Power(15, dayTime, inverted: true);
            var expectedPower = reading.GetProperty("power").GetInt32();
            var expectedInverted = reading.GetProperty("inverted").GetInt32();
            if (power == expectedPower && invertedPower == expectedInverted)
                continue;

            var vanilla = $"{expectedPower}/{expectedInverted}";
            mismatches.Add($"time {dayTime}: vanilla {vanilla}, ours {power}/{invertedPower}");
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
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
            Assert.True(world.Engine.ReceiverHasSignal(detector.Offset(face), face.Opposite()), $"no weak signal toward {face}");

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

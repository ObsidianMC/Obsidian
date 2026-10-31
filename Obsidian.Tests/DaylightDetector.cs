using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Obsidian.API;
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
}

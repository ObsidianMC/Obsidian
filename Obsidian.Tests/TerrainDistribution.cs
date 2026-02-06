using Obsidian.API.Registries;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class TerrainDistributionTests
{
    private readonly ITestOutputHelper output;

    public TerrainDistributionTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void TestTerrainHeightDistribution()
    {
        var settings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
        var finalDensity = settings.NoiseRouter.FinalDensity;

        output.WriteLine("Scanning terrain heights in 2000x2000 area...");

        int underwater = 0;
        int aboveWater = 0;
        int seaLevel = 63;

        for (int x = -1000; x <= 1000; x += 50)
        {
            for (int z = -1000; z <= 1000; z += 50)
            {
                // Find surface by scanning downward
                int surfaceY = -64;
                for (int y = 320; y >= -64; y -= 8)
                {
                    double density = finalDensity.GetValue(x, y, z);
                    if (density > 0.0)
                    {
                        surfaceY = y;
                        break;
                    }
                }

                if (surfaceY < seaLevel)
                    underwater++;
                else
                    aboveWater++;

                output.WriteLine($"({x,4},{z,4}): surface at Y={surfaceY}");
            }
        }

        int total = underwater + aboveWater;
        output.WriteLine($"\n=== TERRAIN DISTRIBUTION ===");
        output.WriteLine($"Total samples: {total}");
        output.WriteLine($"Below sea level (Y<{seaLevel}): {underwater} ({100.0 * underwater / total:F1}%)");
        output.WriteLine($"Above sea level (Y>={seaLevel}): {aboveWater} ({100.0 * aboveWater / total:F1}%)");

        // Minecraft overworld should have roughly 40-60% land
        // If we have 90% underwater, something is wrong
        Assert.True(aboveWater > total * 0.2, $"Too much ocean! Only {100.0 * aboveWater / total:F1}% land");
    }
}

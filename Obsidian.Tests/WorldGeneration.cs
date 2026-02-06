using Obsidian.API.Registries;
using Obsidian.API.World.Generator.Noise;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public class WorldGenerationTests
{
	private readonly ITestOutputHelper output;

	public WorldGenerationTests(ITestOutputHelper output)
	{
		this.output = output;
	}

	[Fact]
	public void TestOverworldDensityFunctions()
	{
		// Get the overworld noise settings
		var overworldSettings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];

		Assert.NotNull(overworldSettings);
		Assert.NotNull(overworldSettings.NoiseRouter);

		var router = overworldSettings.NoiseRouter;

		// Test coordinates: spawn area
		int testX = 0;
		int testZ = 0;

		output.WriteLine("Testing density functions at X=0, Z=0");
		output.WriteLine("=".PadRight(60, '='));

		// Test at various Y levels
		int[] testYLevels = { -64, -32, 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320 };

		foreach (int y in testYLevels)
		{
			output.WriteLine($"\nY = {y}:");

			var continents = router.Continents.GetValue(testX, y, testZ);
			var depth = NoiseRegistry.DensityFunctions.Overworld.Depth.GetValue(testX, y, testZ);
			var offset = NoiseRegistry.DensityFunctions.Overworld.Offset.GetValue(testX, y, testZ);
			var factor = NoiseRegistry.DensityFunctions.Overworld.Factor.GetValue(testX, y, testZ);
			var jaggedness = NoiseRegistry.DensityFunctions.Overworld.Jaggedness.GetValue(testX, y, testZ);
			var base3dNoise = NoiseRegistry.DensityFunctions.Overworld.Base3dNoise.GetValue(testX, y, testZ);
			var slopedCheese = NoiseRegistry.DensityFunctions.Overworld.SlopedCheese.GetValue(testX, y, testZ);
			var finalDensity = router.FinalDensity.GetValue(testX, y, testZ);

			// Calculate what sloped_cheese SHOULD be manually
			var depthPlusJaggedness = depth + jaggedness;
			var timeFactor = depthPlusJaggedness * factor;
			var quarterNeg = timeFactor < 0 ? timeFactor / 4.0 : timeFactor;
			var times4 = quarterNeg * 4.0;
			var expectedSlopedCheese = times4 + base3dNoise;

			output.WriteLine($"  Continents:    {continents,8:F4}");
			output.WriteLine($"  Offset:        {offset,8:F4}");
			output.WriteLine($"  Factor:        {factor,8:F4}");
			output.WriteLine($"  Depth:         {depth,8:F4}");
			output.WriteLine($"  Jaggedness:    {jaggedness,8:F4}");
			output.WriteLine($"  Base3dNoise:   {base3dNoise,8:F4}");
			output.WriteLine($"  SlopedCheese:  {slopedCheese,8:F4} (expected: {expectedSlopedCheese,8:F4})");
			output.WriteLine($"  FinalDensity:  {finalDensity,8:F4}");
			output.WriteLine($"  -> {(finalDensity > 0 ? "SOLID" : "AIR/CAVE")}");
		}

		output.WriteLine("\n" + "=".PadRight(60, '='));

		// Verify basic expectations
		// 1. Continents should vary
		var continentsAtSurface = router.Continents.GetValue(0, 64, 0);
		var continentsAt100BlocksAway = router.Continents.GetValue(100, 64, 100);
		output.WriteLine($"\nContinents at (0,64,0): {continentsAtSurface:F4}");
		output.WriteLine($"Continents at (100,64,100): {continentsAt100BlocksAway:F4}");

		// 2. Offset should change terrain height
		var offsetAtOrigin = NoiseRegistry.DensityFunctions.Overworld.Offset.GetValue(0, 64, 0);
		output.WriteLine($"\nOffset at origin: {offsetAtOrigin:F4}");
		Assert.True(Math.Abs(offsetAtOrigin) < 10, "Offset should be reasonable magnitude");

		// 3. Factor should be non-zero
		var factorAtOrigin = NoiseRegistry.DensityFunctions.Overworld.Factor.GetValue(0, 64, 0);
		output.WriteLine($"Factor at origin: {factorAtOrigin:F4}");
		Assert.True(Math.Abs(factorAtOrigin) > 0.01, "Factor should be non-zero");

		// 4. Deep underground should be solid (positive density)
		var deepDensity = router.FinalDensity.GetValue(0, -32, 0);
		output.WriteLine($"\nDensity at Y=-32: {deepDensity:F4}");
		output.WriteLine($"Expected: SOLID (positive), Actual: {(deepDensity > 0 ? "SOLID" : "AIR/CAVE")}");

		// 5. High in sky should be air (negative density)
		var skyDensity = router.FinalDensity.GetValue(0, 200, 0);
		output.WriteLine($"Density at Y=200: {skyDensity:F4}");
		output.WriteLine($"Expected: AIR (negative), Actual: {(skyDensity > 0 ? "SOLID" : "AIR/CAVE")}");
	}

	[Fact]
	public void TestSplineCoordinateValues()
	{
		var overworldSettings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
		var continents = overworldSettings.NoiseRouter.Continents;

		output.WriteLine("Testing Continents noise at various locations:");
		output.WriteLine("=".PadRight(60, '='));

		// Test continents at a grid of points
		for (int x = -500; x <= 500; x += 100)
		{
			for (int z = -500; z <= 500; z += 100)
			{
				var value = continents.GetValue(x, 64, z);
				output.WriteLine($"Continents({x,4}, 64, {z,4}) = {value,7:F4}");
			}
		}

		output.WriteLine("\n" + "=".PadRight(60, '='));

		// Verify continents has reasonable range
		var values = new List<double>();
		for (int x = 0; x < 1000; x += 50)
		{
			for (int z = 0; z < 1000; z += 50)
			{
				values.Add(continents.GetValue(x, 64, z));
			}
		}

		var min = values.Min();
		var max = values.Max();
		var avg = values.Average();

		output.WriteLine($"\nContinents statistics across 1000x1000 area:");
		output.WriteLine($"  Min: {min:F4}");
		output.WriteLine($"  Max: {max:F4}");
		output.WriteLine($"  Avg: {avg:F4}");
		output.WriteLine($"  Range: {max - min:F4}");

		Assert.True(max - min > 0.5, "Continents should have significant variation");
		Assert.True(min >= -2.0 && max <= 2.0, "Continents should be in reasonable range");
	}

	[Fact]
	public void TestYGradient()
	{
		var overworldSettings = NoiseRegistry.NoiseSettings.All["minecraft:overworld"];
		var depth = NoiseRegistry.DensityFunctions.Overworld.Depth;

		output.WriteLine("Testing Y-gradient component of depth:");
		output.WriteLine("=".PadRight(60, '='));

		// The Y gradient should go from 1.5 at Y=-64 to -1.5 at Y=320
		// At Y=64 (sea level) it should be close to 0

		int[] testYs = { -64, -32, 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320 };

		foreach (int y in testYs)
		{
			var depthValue = depth.GetValue(0, y, 0);
			output.WriteLine($"Depth at Y={y,4}: {depthValue,7:F4}");
		}

		// At Y=-64, gradient alone is 1.5, so depth should be > 1.0 (accounting for offset)
		var depthAtBottom = depth.GetValue(0, -64, 0);
		output.WriteLine($"\nDepth at Y=-64: {depthAtBottom:F4} (should be positive, around 1.5)");

		// At Y=320, gradient alone is -1.5, so depth should be < -1.0
		var depthAtTop = depth.GetValue(0, 320, 0);
		output.WriteLine($"Depth at Y=320: {depthAtTop:F4} (should be negative, around -1.5)");
	}

	[Fact]
	public void TestCaveDensityFunctions()
	{
		output.WriteLine("Testing cave density functions at X=0, Z=0:");
		output.WriteLine("=".PadRight(60, '='));

		int[] testYs = { -32, 0, 32, 64, 96, 128 };

		foreach (int y in testYs)
		{
			output.WriteLine($"\nY = {y}:");

			var slopedCheese = NoiseRegistry.DensityFunctions.Overworld.SlopedCheese.GetValue(0, y, 0);
			var entrances = NoiseRegistry.DensityFunctions.Overworld.Caves.Entrances.GetValue(0, y, 0);
			var spaghetti2d = NoiseRegistry.DensityFunctions.Overworld.Caves.Spaghetti2d.GetValue(0, y, 0);
			var pillars = NoiseRegistry.DensityFunctions.Overworld.Caves.Pillars.GetValue(0, y, 0);
			var noodle = NoiseRegistry.DensityFunctions.Overworld.Caves.Noodle.GetValue(0, y, 0);
			var finalDensity = NoiseRegistry.NoiseSettings.All["minecraft:overworld"].NoiseRouter.FinalDensity.GetValue(0, y, 0);

			output.WriteLine($"  SlopedCheese:  {slopedCheese,8:F4}");
			output.WriteLine($"  Entrances:     {entrances,8:F4}");
			output.WriteLine($"  Spaghetti2D:   {spaghetti2d,8:F4}");
			output.WriteLine($"  Pillars:       {pillars,8:F4}");
			output.WriteLine($"  Noodle:        {noodle,8:F4}");
			output.WriteLine($"  FinalDensity:  {finalDensity,8:F4}");
			output.WriteLine($"  -> {(finalDensity > 0 ? "SOLID" : "CAVE/AIR")}");
		}

		output.WriteLine("\n" + "=".PadRight(60, '='));
		output.WriteLine("\nExpected: Cave functions should return values close to 0 most of the time,");
		output.WriteLine("and only carve caves (large negative values) in specific areas.");
		output.WriteLine("Noodle caves should not clamp everything to -0.458!");
	}
}

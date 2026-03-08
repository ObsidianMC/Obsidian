using Obsidian.API.World.Generator.Noise;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Samples climate parameters from density functions in the noise router.
/// Single Responsibility: Convert world coordinates to climate parameter space.
/// </summary>
internal sealed class ClimateSampler
{
	private readonly NoiseRouter _noiseRouter;

	public ClimateSampler(NoiseRouter noiseRouter)
	{
		_noiseRouter = noiseRouter ?? throw new ArgumentNullException(nameof(noiseRouter));
	}

	/// <summary>
	/// Samples all climate parameters at the given world coordinates.
	/// </summary>
	/// <param name="x">World X coordinate</param>
	/// <param name="y">World Y coordinate (for depth calculation)</param>
	/// <param name="z">World Z coordinate</param>
	/// <returns>Climate parameters at this location</returns>
	public Climate Sample(int x, int y, int z)
	{
		return new Climate
		{
			Temperature = _noiseRouter.Temperature.GetValue(x, y, z),
			Humidity = _noiseRouter.Vegetation.GetValue(x, y, z),
			Continentalness = _noiseRouter.Continents.GetValue(x, y, z),
			Erosion = _noiseRouter.Erosion.GetValue(x, y, z),
			Weirdness = _noiseRouter.Ridges.GetValue(x, y, z),
			Depth = CalculateDepth(y)
		};
	}

	/// <summary>
	/// Calculates the depth parameter based on Y coordinate.
	/// Surface (Y=63) = 0, deeper is negative, higher is positive.
	/// </summary>
	private static double CalculateDepth(int y)
	{
		const int seaLevel = 63;
		return (y - seaLevel) / 64.0; // Normalize to roughly -1 to 1 range
	}
}

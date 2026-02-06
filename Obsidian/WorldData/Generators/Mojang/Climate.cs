namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Represents a point in 5D climate parameter space used for biome selection.
/// Each parameter typically ranges from -1.0 to 1.0 but can exceed these bounds.
/// </summary>
public readonly record struct Climate
{
	/// <summary>
	/// Temperature parameter. Lower values = colder biomes, higher = warmer.
	/// Sampled from NoiseRouter.Temperature
	/// </summary>
	public required double Temperature { get; init; }

	/// <summary>
	/// Humidity/Vegetation parameter. Lower = drier, higher = wetter.
	/// Sampled from NoiseRouter.Vegetation
	/// </summary>
	public required double Humidity { get; init; }

	/// <summary>
	/// Continentalness parameter. Lower = ocean, higher = inland/mountains.
	/// Sampled from NoiseRouter.Continents
	/// </summary>
	public required double Continentalness { get; init; }

	/// <summary>
	/// Erosion parameter. Lower = flat/valleys, higher = peaks.
	/// Sampled from NoiseRouter.Erosion
	/// </summary>
	public required double Erosion { get; init; }

	/// <summary>
	/// Weirdness parameter. Controls unusual terrain features and biome variants.
	/// Sampled from NoiseRouter.Ridges
	/// </summary>
	public required double Weirdness { get; init; }

	/// <summary>
	/// Depth parameter used for vertical biome variation (caves).
	/// For overworld surface biomes, this is typically 0.
	/// </summary>
	public required double Depth { get; init; }

	/// <summary>
	/// Calculates the squared distance between two climate points in parameter space.
	/// Used for finding the closest matching biome.
	/// </summary>
	public double DistanceSquared(Climate other)
	{
		double tempDiff = Temperature - other.Temperature;
		double humidDiff = Humidity - other.Humidity;
		double contDiff = Continentalness - other.Continentalness;
		double erosDiff = Erosion - other.Erosion;
		double weirdDiff = Weirdness - other.Weirdness;
		double depthDiff = Depth - other.Depth;

		return tempDiff * tempDiff +
			   humidDiff * humidDiff +
			   contDiff * contDiff +
			   erosDiff * erosDiff +
			   weirdDiff * weirdDiff +
			   depthDiff * depthDiff;
	}
}

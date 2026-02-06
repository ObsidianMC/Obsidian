using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Represents a biome mapped to specific climate parameters.
/// Used in multi-noise biome selection.
/// </summary>
internal sealed record BiomeParameterPoint
{
	public required BiomeCodec Biome { get; init; }
	public required Climate Climate { get; init; }

	/// <summary>
	/// Calculates how well this biome matches the given climate.
	/// Lower values = better match.
	/// </summary>
	public double FitnessDistance(Climate target)
	{
		return Climate.DistanceSquared(target);
	}
}

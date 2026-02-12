using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Interface for biome selection strategies.
/// Interface Segregation Principle: Only exposes biome selection capability.
/// Open/Closed Principle: Allows different biome source implementations.
/// </summary>
public interface IBiomeSource
{
	/// <summary>
	/// Gets the biome at the specified world coordinates.
	/// </summary>
	/// <param name="x">World X coordinate</param>
	/// <param name="y">World Y coordinate</param>
	/// <param name="z">World Z coordinate</param>
	/// <returns>The biome at this location</returns>
	BiomeCodec GetBiome(int x, int y, int z);
}

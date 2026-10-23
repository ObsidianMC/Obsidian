namespace Obsidian.API.World.Generator;

/// <summary>
/// A biome source type a plugin adds, like a NeoForge mod's <c>BiomeSource</c>: a level stem's <c>biome_source</c>
/// names it in its <c>type</c>, and the object's other keys set its properties. Register it with
/// <c>IPluginRegistry.RegisterWorldgenType&lt;BiomeSourceBase, TSource&gt;</c>.
/// </summary>
/// <remarks>
/// Biomes are chosen per quart, a 4x4x4 block cell, like vanilla's. <see cref="GetBiome"/> runs for every quart of
/// every chunk generated, and for biome lookups, on several threads at once, so keep it fast and don't change the
/// source's state in it.
/// </remarks>
public abstract class BiomeSourceBase
{
    /// <summary>
    /// The type's identifier, the one it's registered with.
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Every biome <see cref="GetBiome"/> can return, vanilla's or plugins', like <c>minecraft:plains</c>. Read once,
    /// when the dimension is read; its order orders the features of the dimension's biomes, like vanilla's
    /// <c>possibleBiomes</c>.
    /// </summary>
    public abstract IReadOnlyList<string> PossibleBiomes { get; }

    /// <summary>
    /// The biome at quart coordinates (block coordinates divided by 4), one of <see cref="PossibleBiomes"/>.
    /// </summary>
    /// <param name="climate">Samples the noise settings' climate.</param>
    public abstract string GetBiome(int quartX, int quartY, int quartZ, IClimateSampler climate);
}

/// <summary>
/// Samples the climate of the noise settings' router, like vanilla's <c>Climate.Sampler</c>.
/// </summary>
public interface IClimateSampler
{
    /// <summary>
    /// The climate at the block position of quart coordinates.
    /// </summary>
    public ClimatePoint Sample(int quartX, int quartY, int quartZ);
}

/// <summary>
/// The router's climate values at a position, before vanilla quantizes them to pick a multi-noise biome: mostly
/// from -1 to 1, the range of vanilla's biome parameters.
/// </summary>
/// <param name="Weirdness">The router's <c>ridges</c>.</param>
/// <param name="Humidity">The router's <c>vegetation</c>.</param>
public readonly record struct ClimatePoint(
    double Temperature,
    double Humidity,
    double Continentalness,
    double Erosion,
    double Depth,
    double Weirdness);

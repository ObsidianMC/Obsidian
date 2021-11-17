// Thank you https://github.com/rthome/SharpNoise/tree/master/Examples/ComplexPlanetExample

namespace Obsidian.WorldData.Generators.Overworld;

public class OverworldTerrainSettings
{
    /// <summary>
    /// Planet seed.  Change this to generate a different planet.
    /// </summary>
    public int Seed { get; set; } = 133769420;

    /// <summary>
    /// Minimum elevation on the planet, in meters.  This value is approximate.
    /// </summary>
    public double MinElev { get; set; } = 0;

    /// <summary>
    /// Maximum elevation on the planet, in meters.  This value is approximate.
    /// </summary>
    public double MaxElev { get; set; } = 128;

    /// <summary>
    /// Frequency of the planet's continents.  Higher frequency produces smaller,
    /// more numerous continents.  This value is measured in radians.
    /// </summary>

    public double ContinentLacunarity { get; set; } = 2.508984375;

    /// <summary>
    /// Lacunarity of the planet's mountains.  Changing this value produces
    /// slightly different mountains.  For the best results, this value should
    /// be random, but close to 2.0.
    /// </summary>
    public double MountainLacunarity { get; set; } = 2.03;

    /// <summary>
    /// Lacunarity of the planet's plains.  Changing this value produces slightly
    /// different plains.  For the best results, this value should be random, but
    /// close to 2.0.
    /// </summary>
    public double PlainsLacunarity { get; set; } = 1.014453125;

    /// <summary>
    /// Lacunarity of the planet's badlands.  Changing this value produces
    /// slightly different badlands.  For the best results, this value should be
    /// random, but close to 2.0.
    /// </summary>
    public double BadlandsLacunarity { get; set; } = 1.200890625;

    /// <summary>
    /// Specifies the "twistiness" of the mountains.
    /// </summary>
    public double MountainsTwist { get; set; } = 2.1337;

    /// <summary>
    /// Specifies the "twistiness" of the badlands.
    /// </summary>
    public double BadlandsTwist { get; set; } = 2.9876;

    /// <summary>
    /// Specifies the amount of "glaciation" on the mountains.  This value
    /// should be close to 1.0 and greater than 1.0.
    /// </summary>
    public double MountainGlaciation { get; set; } = 1.075;

    /// <summary>
    /// 0 for equal amounts land/ocean
    /// 0 -> 0.5 for more land
    /// -0.5 -> 0 for more ocean
    /// </summary>
    public double OceanLandRatio { get; set; } = 0.3;

    /// <summary>
    /// Size of Continents (and Oceans)
    /// 4.0 is normal.
    /// Increase for larger continents
    /// Decrease for smaller
    /// </summary>
    public double ContinentSize { get; set; } = 1.0;

    /// <summary>
    /// 0 for equal amounts of wet/average/dry biomes
    /// 0 -> 0.5 for more dry biomes
    /// -0.5 -> 0 for more wet biomes
    /// </summary>
    public double BiomeHumidityRatio { get; set; } = 0.0;

    /// <summary>
    /// 0 for equal amounts of cold/average/hot biomes
    /// 0 -> 0.5 for more hot biomes
    /// -0.5 -> 0 for more cold biomes
    /// </summary>
    public double BiomeTemperatureRatio { get; set; } = 0.1;

    /// <summary>
    /// 0 for equal amounts of plain/hilly/mountainous biomes
    /// 0 -> 0.5 for more mountains biomes
    /// -0.5 -> 0 for more plains biomes
    /// </summary>
    public double BiomeTerrainRatio { get; set; } = -0.3;

    /// <summary>
    /// Size of Biomes
    /// 4.0 is normal
    /// Increase for larger biomes
    /// Decrease for smaller biomes
    /// </summary>
    public double BiomeSize { get; set; } = 4.1;

    public int WaterLevel { get; set; } = 64;

    public OverworldTerrainSettings()
    {
    }
}

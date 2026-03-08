using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.Noise;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Multi-noise biome source that selects biomes based on climate parameters.
/// Single Responsibility: Maps climate parameters to biomes.
/// Dependency Inversion: Depends on abstractions (NoiseRouter) not concretions.
/// </summary>
internal sealed class MultiNoiseBiomeSource : IBiomeSource
{
    private readonly ClimateSampler _climateSampler;
    private readonly List<BiomeParameterPoint> _biomePoints;

    public MultiNoiseBiomeSource(NoiseRouter noiseRouter)
    {
        _climateSampler = new ClimateSampler(noiseRouter);
        _biomePoints = BuildOverworldBiomeParameters();
    }

    public BiomeCodec GetBiome(int x, int y, int z)
    {
        var targetClimate = _climateSampler.Sample(x, y, z);
        return FindClosestBiome(targetClimate);
    }

    /// <summary>
    /// Finds the biome with climate parameters closest to the target.
    /// </summary>
    private BiomeCodec FindClosestBiome(Climate target)
    {
        BiomeCodec? bestBiome = null;
        double bestDistance = double.MaxValue;

        foreach (var point in _biomePoints)
        {
            double distance = point.FitnessDistance(target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBiome = point.Biome;
            }
        }

        return bestBiome ?? CodecRegistry.Biomes.Plains; // Fallback
    }

    /// <summary>
    /// Builds the overworld biome parameter list.
    /// This approximates Minecraft's multi-noise biome distribution.
    /// 
    /// Parameters generally follow:
    /// - Temperature: -1.0 (frozen) to 1.0 (hot)
    /// - Humidity: -1.0 (dry) to 1.0 (wet)
    /// - Continentalness: -1.0 (deep ocean) to 1.0 (inland)
    /// - Erosion: -1.0 (valleys) to 1.0 (peaks)
    /// - Weirdness: -1.0 to 1.0 (affects terrain variation)
    /// </summary>
    private static List<BiomeParameterPoint> BuildOverworldBiomeParameters()
    {
        var points = new List<BiomeParameterPoint>();

        // Ocean biomes - very negative continentalness
        AddOceanBiomes(points);

        // Coastal/beach biomes - slightly negative continentalness
        AddCoastalBiomes(points);

        // Plains and flatland biomes - near-zero continentalness, low erosion
        AddPlainsBiomes(points);

        // Forest biomes - moderate continentalness and humidity
        AddForestBiomes(points);

        // Mountain biomes - high erosion
        AddMountainBiomes(points);

        // Desert and arid biomes - low humidity
        AddDesertBiomes(points);

        // Jungle biomes - high temperature and humidity
        AddJungleBiomes(points);

        // Snowy biomes - very low temperature
        AddSnowyBiomes(points);

        // Swamp biomes - high humidity, low erosion
        AddSwampBiomes(points);

        // Badlands biomes - low humidity, high continentalness
        AddBadlandsBiomes(points);

        return points;
    }

    private static void AddOceanBiomes(List<BiomeParameterPoint> points)
    {
        // Deep oceans
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.DeepFrozenOcean,
            Climate = new Climate { Temperature = -0.8, Humidity = 0.0, Continentalness = -0.8, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.DeepColdOcean,
            Climate = new Climate { Temperature = -0.3, Humidity = 0.0, Continentalness = -0.8, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.DeepOcean,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.0, Continentalness = -0.8, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.DeepLukewarmOcean,
            Climate = new Climate { Temperature = 0.6, Humidity = 0.0, Continentalness = -0.8, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });

        // Normal oceans
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FrozenOcean,
            Climate = new Climate { Temperature = -0.8, Humidity = 0.0, Continentalness = -0.5, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.ColdOcean,
            Climate = new Climate { Temperature = -0.3, Humidity = 0.0, Continentalness = -0.5, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Ocean,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.0, Continentalness = -0.5, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.LukewarmOcean,
            Climate = new Climate { Temperature = 0.6, Humidity = 0.0, Continentalness = -0.5, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WarmOcean,
            Climate = new Climate { Temperature = 0.9, Humidity = 0.0, Continentalness = -0.5, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddCoastalBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Beach,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.0, Continentalness = -0.2, Erosion = -0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SnowyBeach,
            Climate = new Climate { Temperature = -0.5, Humidity = 0.0, Continentalness = -0.2, Erosion = -0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.StonyShore,
            Climate = new Climate { Temperature = 0.0, Humidity = 0.0, Continentalness = -0.2, Erosion = 0.5, Weirdness = 0.0, Depth = 0.0 }
        });

        // Rivers - Need multiple points with varying continentalness to capture rivers across the landscape
        // Rivers occur in valleys (high erosion) across various continental settings

        // Rivers in ocean-adjacent areas
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.River,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.5, Continentalness = -0.15, Erosion = -0.6, Weirdness = 0.0, Depth = 0.0 }
        });

        // Rivers inland (most common)
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.River,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.4, Continentalness = 0.0, Erosion = -0.7, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.River,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.5, Continentalness = 0.15, Erosion = -0.65, Weirdness = 0.0, Depth = 0.0 }
        });

        // Rivers in continental areas
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.River,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.5, Continentalness = 0.3, Erosion = -0.7, Weirdness = 0.0, Depth = 0.0 }
        });

        // Frozen rivers - similar pattern but cold
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FrozenRiver,
            Climate = new Climate { Temperature = -0.6, Humidity = 0.5, Continentalness = -0.15, Erosion = -0.6, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FrozenRiver,
            Climate = new Climate { Temperature = -0.5, Humidity = 0.4, Continentalness = 0.0, Erosion = -0.7, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FrozenRiver,
            Climate = new Climate { Temperature = -0.6, Humidity = 0.5, Continentalness = 0.3, Erosion = -0.7, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddPlainsBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Plains,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.2, Continentalness = 0.0, Erosion = -0.2, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SunflowerPlains,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.2, Continentalness = 0.0, Erosion = -0.2, Weirdness = 0.5, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SnowyPlains,
            Climate = new Climate { Temperature = -0.6, Humidity = 0.2, Continentalness = 0.0, Erosion = -0.2, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.IceSpikes,
            Climate = new Climate { Temperature = -0.8, Humidity = 0.1, Continentalness = 0.1, Erosion = -0.2, Weirdness = 0.6, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.MushroomFields,
            Climate = new Climate { Temperature = 0.4, Humidity = 0.5, Continentalness = -0.1, Erosion = 0.0, Weirdness = 0.8, Depth = 0.0 }
        });
    }

    private static void AddForestBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Forest,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.6, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FlowerForest,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.6, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.4, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.BirchForest,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.5, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.OldGrowthBirchForest,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.5, Continentalness = 0.2, Erosion = 0.1, Weirdness = 0.5, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.DarkForest,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.7, Continentalness = 0.3, Erosion = 0.2, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Taiga,
            Climate = new Climate { Temperature = -0.2, Humidity = 0.5, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SnowyTaiga,
            Climate = new Climate { Temperature = -0.6, Humidity = 0.4, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.OldGrowthPineTaiga,
            Climate = new Climate { Temperature = -0.2, Humidity = 0.5, Continentalness = 0.3, Erosion = 0.2, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.OldGrowthSpruceTaiga,
            Climate = new Climate { Temperature = -0.3, Humidity = 0.6, Continentalness = 0.3, Erosion = 0.2, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddMountainBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WindsweptHills,
            Climate = new Climate { Temperature = 0.0, Humidity = 0.3, Continentalness = 0.3, Erosion = 0.5, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WindsweptGravellyHills,
            Climate = new Climate { Temperature = 0.0, Humidity = 0.2, Continentalness = 0.3, Erosion = 0.6, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WindsweptForest,
            Climate = new Climate { Temperature = 0.0, Humidity = 0.5, Continentalness = 0.3, Erosion = 0.5, Weirdness = 0.0, Depth = 0.0 }
        });

        // High peaks
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.JaggedPeaks,
            Climate = new Climate { Temperature = -0.4, Humidity = 0.2, Continentalness = 0.4, Erosion = 0.8, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.FrozenPeaks,
            Climate = new Climate { Temperature = -0.6, Humidity = 0.3, Continentalness = 0.4, Erosion = 0.8, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.StonyPeaks,
            Climate = new Climate { Temperature = 0.3, Humidity = 0.2, Continentalness = 0.4, Erosion = 0.8, Weirdness = 0.0, Depth = 0.0 }
        });

        // Mountain slopes and plateaus
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SnowySlopes,
            Climate = new Climate { Temperature = -0.5, Humidity = 0.3, Continentalness = 0.3, Erosion = 0.6, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Grove,
            Climate = new Climate { Temperature = -0.4, Humidity = 0.5, Continentalness = 0.3, Erosion = 0.5, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Meadow,
            Climate = new Climate { Temperature = 0.1, Humidity = 0.6, Continentalness = 0.2, Erosion = 0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.CherryGrove,
            Climate = new Climate { Temperature = 0.2, Humidity = 0.6, Continentalness = 0.2, Erosion = 0.3, Weirdness = 0.3, Depth = 0.0 }
        });
    }

    private static void AddDesertBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Desert,
            Climate = new Climate { Temperature = 0.9, Humidity = -0.6, Continentalness = 0.1, Erosion = -0.1, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Savanna,
            Climate = new Climate { Temperature = 0.7, Humidity = -0.3, Continentalness = 0.1, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SavannaPlateau,
            Climate = new Climate { Temperature = 0.7, Humidity = -0.3, Continentalness = 0.3, Erosion = 0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WindsweptSavanna,
            Climate = new Climate { Temperature = 0.7, Humidity = -0.3, Continentalness = 0.3, Erosion = 0.6, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddJungleBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Jungle,
            Climate = new Climate { Temperature = 0.8, Humidity = 0.8, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SparseJungle,
            Climate = new Climate { Temperature = 0.8, Humidity = 0.6, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.BambooJungle,
            Climate = new Climate { Temperature = 0.8, Humidity = 0.8, Continentalness = 0.2, Erosion = 0.0, Weirdness = 0.5, Depth = 0.0 }
        });
    }

    private static void AddSnowyBiomes(List<BiomeParameterPoint> points)
    {
        // Already added some in other methods, but ensure coverage
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.SnowyPlains,
            Climate = new Climate { Temperature = -0.7, Humidity = 0.3, Continentalness = 0.1, Erosion = -0.1, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddSwampBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Swamp,
            Climate = new Climate { Temperature = 0.4, Humidity = 0.8, Continentalness = 0.0, Erosion = -0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.MangroveSwamp,
            Climate = new Climate { Temperature = 0.6, Humidity = 0.9, Continentalness = -0.1, Erosion = -0.3, Weirdness = 0.0, Depth = 0.0 }
        });
    }

    private static void AddBadlandsBiomes(List<BiomeParameterPoint> points)
    {
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.Badlands,
            Climate = new Climate { Temperature = 0.9, Humidity = -0.7, Continentalness = 0.4, Erosion = 0.2, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.WoodedBadlands,
            Climate = new Climate { Temperature = 0.9, Humidity = -0.5, Continentalness = 0.4, Erosion = 0.3, Weirdness = 0.0, Depth = 0.0 }
        });
        points.Add(new BiomeParameterPoint
        {
            Biome = CodecRegistry.Biomes.ErodedBadlands,
            Climate = new Climate { Temperature = 0.9, Humidity = -0.7, Continentalness = 0.4, Erosion = 0.5, Weirdness = 0.6, Depth = 0.0 }
        });
    }
}

using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class DeepOceanTerrain : BaseTerrain
{
    // Generates the Ocean terrain.
    // Outputs will be between -0.02 and -0.5
    public DeepOceanTerrain() : base()
    {
        this.Result = new Cache
        {
            Source0 = new ScalePoint
            {
                XScale = 1 / 140.103,
                YScale = 1 / 140.103,
                ZScale = 1 / 140.103,
                Source0 = new Clamp
                {
                    UpperBound = 0.0,
                    LowerBound = -1.0,
                    Source0 = new Turbulence
                    {
                        Frequency = 39.4578,
                        Power = 0.078,
                        Roughness = 3,
                        Seed = settings.Seed + 72,
                        Source0 = new Multiply
                        {
                            Source0 = new ScaleBias
                            {
                                Scale = 0.01, // Flatten
                                Bias = -1.0, // move elevation
                                Source0 = new Billow
                                {
                                    Seed = settings.Seed + 70,
                                    Frequency = 10.5,
                                    Persistence = 0.5,
                                    Lacunarity = settings.PlainsLacunarity,
                                    OctaveCount = 3,
                                    Quality = NoiseQuality.Standard,
                                }
                            },
                            Source1 = new ScaleBias
                            {
                                Scale = 0.2,
                                Bias = 0.6,
                                Source0 = new Billow
                                {
                                    Seed = settings.Seed + 71,
                                    Frequency = 3.5,
                                    Persistence = 0.5,
                                    Lacunarity = settings.PlainsLacunarity,
                                    OctaveCount = 4,
                                    Quality = NoiseQuality.Fast,
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}

using SharpNoise.Modules;

namespace Obsidian.API.Noise;
internal class OverworldTerrain : Module
{
    public int Seed { get; set; }

    public double TerrainStretch { get; set; } = 8.1037;

    protected readonly Module terrainPerlin;

    public OverworldTerrain(Module height, Module squash, Module erosion) : base(3)
    {
        SourceModules[0] = height;
        SourceModules[1] = squash;
        SourceModules[2] = erosion;
        terrainPerlin = new Turbulence()
        {
            Frequency = 1/TerrainStretch,
            Power = 2,
            Roughness = 3,
            Seed = Seed + 1,
            Source0 = new Perlin()
            {
                Frequency = 0.333/TerrainStretch,
                OctaveCount = 3,
                Lacunarity = 0.8899,
                Persistence = 0.1334,
                Quality = SharpNoise.NoiseQuality.Fast,
                Seed = Seed + 2
            }
        };
    }

    public override double GetValue(double x, double y, double z)
    {
        var squash = SourceModules[1].GetValue(x, 0, z) + 1.1d; // Can't be zero
        var height = SourceModules[0].GetValue(x, 0, z);

        // Beash/Ocean flat, everything else amplified
        squash = height < 0.02 ? squash * 0.5d : Math.Pow(squash, 7);

        var result = terrainPerlin.GetValue(x, y, z);
        if (height > 0) // If above ocean, add erosion
        {
            height *= (SourceModules[2].GetValue(x, 0, z) + 0.6) * 10.0 ;
        }
        double yOffset = y + (height * 128 * -1);
        double bias = yOffset - 192; // put half world height to 0
        bias = Math.Pow(bias, 3) / squash;
        bias /= 192d; // world height to -1 < y < 1

        

        result -= bias;

        return Math.Clamp(result, -1, 1);
    }
}

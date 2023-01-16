using SharpNoise.Modules;

namespace Obsidian.API.Noise;
internal class OverworldTerrain : Module
{
    public int Seed { get; set; }

    public double TerrainStretch { get; set; } = 8.1037;

    protected readonly Module terrainPerlin;

    public OverworldTerrain(Module height, Module erosion) : base(2)
    {
        SourceModules[0] = height;
        SourceModules[1] = erosion;
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
        var erosion = SourceModules[1].GetValue(x, 0, z) + 1.1d; // Can't be zero
        var height = SourceModules[0].GetValue(x, 0, z);
        if (height >= 0.02)
        {
            erosion = Math.Pow(erosion, 5);
        } else
        {
            erosion *= 0.5d;
        }
        double yOffset = y + (height * -128);
        double bias = yOffset - 192; // put half world height to 0
        bias = Math.Pow(bias, 3) / erosion;
        bias = bias / 192d; // world height to -1 < y < 1
        var result = Math.Clamp(terrainPerlin.GetValue(x, y, z) - bias, -1, 1);

        if (result >= 0.2)
        {

        }

        return result;
    }
}

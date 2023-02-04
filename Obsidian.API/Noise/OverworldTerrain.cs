using SharpNoise.Modules;

namespace Obsidian.API.Noise;
internal class OverworldTerrain : Module
{
    public int Seed { get; set; }

    public double TerrainStretch { get; set; } = 8.1037;

    protected readonly Module terrainPerlin;

    public OverworldTerrain(Module height, Module squash, Module erosion, Module river, Module peaks) : base(5)
    {
        SourceModules[0] = height;
        SourceModules[1] = squash;
        SourceModules[2] = erosion;
        SourceModules[3] = river;
        SourceModules[4] = peaks;

        terrainPerlin = new Perlin()
        {
            Frequency = 0.333 / TerrainStretch,
            OctaveCount = 3,
            Lacunarity = 0.8899,
            Persistence = 0.1334,
            Quality = SharpNoise.NoiseQuality.Fast,
            Seed = Seed + 2
        };
    }

    public override double GetValue(double x, double y, double z)
    {
        var squash = SourceModules[1].GetValue(x, 0, z) + 1.1d; // Can't be zero
        var height = SourceModules[0].GetValue(x, 0, z);
        var erosionVal = SourceModules[2].GetValue(x, 0, z) + 2.0;


        if (height > 0.1) // If above ocean, add erosion and rivers
        {
            erosionVal = (height - 0.1) * erosionVal;
            height += erosionVal;
        }

        if (height > -0.1)
        {
            var riverVal = SourceModules[3].GetValue(x, 0, z);
            height = Math.Min(height, riverVal);
        }

        // Beash/Ocean flat, everything else amplified
        squash = height < 0.1 ? squash * 0.3d : 1.12 * Math.Pow(squash,3);
        if (height >= 0.6) // Add mountain peaks/valleys
        {
            var peakVal = (height - 0.6) * Math.Max(SourceModules[4].GetValue(x, 0, z) + 1.6, 1.0)*0.5;
            height += peakVal;// * (erosionVal - 0.5);
        }

        double yOffset = y + (height * 128 * -1);
        double bias = yOffset - 192; // put half world height to 0
        bias = Math.Pow(bias, 3) / squash;
        bias /= 192d; // world height to -1 < y < 1
        var result = terrainPerlin.GetValue(x, y, z);
        result -= bias;

        return Math.Clamp(result, -1, 1);
    }
}

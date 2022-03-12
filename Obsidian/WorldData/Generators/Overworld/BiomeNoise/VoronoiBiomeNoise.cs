using Obsidian.API.Noise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.BiomeNoise;

internal class VoronoiBiomeNoise
{
    private static readonly Lazy<VoronoiBiomeNoise> lazy = new(() => new VoronoiBiomeNoise());

    public static VoronoiBiomeNoise Instance { get { return lazy.Value; } }

    private readonly int seed;

    internal readonly Module result;

    internal VoronoiBiomeNoise()
    {
        seed = OverworldGenerator.GeneratorSettings.Seed;

        var pass1 = new Turbulence
        {
            Frequency = 0.007119,
            Power = 50,
            Roughness = 3,
            Seed = seed + 123,
            Source0 = new VoronoiBiomes
            {
                Frequency = 0.0054159,
                Seed = seed
            }
        };

        result = new Cache
        {
            Source0 = pass1
        };
    }
}

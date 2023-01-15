using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class TerrainSelect : SharpNoise.Modules.Blend
{
    private static readonly Constant zero = new() { ConstantValue = 0 };

    public Module BiomeSelector { get; set; }
    internal Dictionary<Biome, Module> TerrainModules { get; set; } = new();

    public TerrainSelect(Module biomeSelector)
    {
        Source1 = zero;
        BiomeSelector = biomeSelector;
    }

    public override double GetValue(double x, double y, double z)
    {
        var b = (int)BiomeSelector.GetValue(x, y, z);
        Source0 = TerrainModules.TryGetValue((Biome)b, out Module? terrainModule) ? terrainModule : zero;
        return base.GetValue(x, y, z);
    }
}

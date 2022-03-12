using SharpNoise.Modules;
using System.Diagnostics;

namespace Obsidian.API.Noise;

public class TerrainSelect : SharpNoise.Modules.Blend
{
    private static readonly Constant zero = new() { ConstantValue = -0.02 };

    public Module BiomeSelector { get; set; }
    internal Dictionary<Biomes, Module> TerrainModules { get; set; } = new();

    public TerrainSelect(Module biomeSelector)
    {
        Source1 = zero;
        BiomeSelector = biomeSelector;
    }

    public override double GetValue(double x, double y, double z)
    {
        var b = (int)BiomeSelector.GetValue(x, y, z);
        Source0 = TerrainModules.TryGetValue((Biomes)b, out Module? terrainModule) ? terrainModule : zero;
        var val = base.GetValue(x, y, z);
        if (val > 0.7) { Debugger.Break(); }
        return val;
    }
}

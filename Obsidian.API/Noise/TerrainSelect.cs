using SharpNoise.Modules;

namespace Obsidian.API.Noise;

public class TerrainSelect : SharpNoise.Modules.Blend
{
    internal Dictionary<Biomes, Module> TerrainModules { get; set; } = new();

    public Module BiomeSelector { get; set; }

    public TerrainSelect(Module biomeSelector)
    {
        Source1 = new Constant { ConstantValue = 0 };
        BiomeSelector = biomeSelector;
    }

    public override double GetValue(double x, double y, double z)
    {
        var b = (int)BiomeSelector.GetValue(x, y, z);
        Source0 = Enum.IsDefined(typeof(Biomes), b) ? TerrainModules[(Biomes)b] : new Constant { ConstantValue = 0 };
        return base.GetValue(x, y, z);
    }
}

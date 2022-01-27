using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class BaseTerrain : Module
{
    protected readonly OverworldTerrainSettings settings;

    protected Module result;

    protected BaseTerrain() : base(0)
    {
        settings = OverworldGenerator.GeneratorSettings;
        result = new Constant { ConstantValue = 0 };
    }

    public override double GetValue(double x, double y, double z)
    {
        return result.GetValue(x, y, z);
    }
}

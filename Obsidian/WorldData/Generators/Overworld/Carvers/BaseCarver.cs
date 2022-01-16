
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Carvers;

public class BaseCarver : Module
{
    public Module result;

    protected readonly OverworldTerrainSettings settings;

    protected BaseCarver() : base(0)
    {
        this.settings = OverworldGenerator.GeneratorSettings;
        result = new Constant { ConstantValue = 0 };
    }

    public override double GetValue(double x, double y, double z)
    {
        return result.GetValue(x, y, z);
    }
}

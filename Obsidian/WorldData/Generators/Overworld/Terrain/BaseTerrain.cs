using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public partial class BaseTerrain : Module
{

    protected readonly int seed;
    protected readonly OverworldTerrainSettings settings;
    protected Cache result;

    protected BaseTerrain(int seed, OverworldTerrainSettings settings) : base(0)
    {
        this.seed = seed;
        this.settings = settings;
    }

    public override double GetValue(double x, double y, double z) => result.GetValue(x, y, z);
}

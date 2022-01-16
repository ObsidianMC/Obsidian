using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class DeepOceanTerrain : BaseTerrain
{
    // Generates the Ocean terrain.
    // Outputs will be between -0.02 and -0.5
    public DeepOceanTerrain() : base()
    {
        result = new Add()
        {
            Source0 = new PlainsTerrain(),
            Source1 = new Constant() { ConstantValue = -0.45f }
        };
    }
}

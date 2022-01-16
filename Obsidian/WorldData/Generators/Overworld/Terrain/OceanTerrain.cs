using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class OceanTerrain : BaseTerrain
{
    public OceanTerrain() : base()
    {
        result = new Add()
        {
            Source0 = new PlainsTerrain(),
            Source1 = new Constant() { ConstantValue = -0.25f }
        };
    }
}

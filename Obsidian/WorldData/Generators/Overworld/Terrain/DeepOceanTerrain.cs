using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class DeepOceanTerrain : BaseTerrain
{
    // Generates the Ocean terrain.
    // Outputs will be between -0.02 and -0.5
    public DeepOceanTerrain(int seed, OverworldTerrainSettings settings) : base(seed, settings)
    {
        result = new Cache()
        {
            Source0 = new Add()
            {
                Source0 = new PlainsTerrain(seed, settings),
                Source1 = new Constant() { ConstantValue = -0.45f }
            }
        };
    }
}

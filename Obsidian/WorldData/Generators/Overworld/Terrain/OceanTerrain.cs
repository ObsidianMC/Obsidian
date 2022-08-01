using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class OceanTerrain : BaseTerrain
{
    public OceanTerrain(int seed, OverworldTerrainSettings settings) : base(seed, settings)
    {
        result = new Cache()
        {
            Source0 = new Add()
            {
                Source0 = new PlainsTerrain(seed, settings),
                Source1 = new Constant() { ConstantValue = -0.25f }
            }
        };
    }
}

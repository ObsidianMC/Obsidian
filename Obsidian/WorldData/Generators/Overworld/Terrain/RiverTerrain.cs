using SharpNoise;
using SharpNoise.Modules;

namespace Obsidian.WorldData.Generators.Overworld.Terrain;

public class RiverTerrain : BaseTerrain
{
    // Generates the plains terrain.
    // Outputs will be between -0.3 and -0.1
    public RiverTerrain(int seed, OverworldTerrainSettings settings) : base(seed, settings)
    {
        result = new Cache()
        {
            Source0 = new ScalePoint()
            {
                XScale = 4.0,
                ZScale = 4.0,
                Source0 = new ScaleBias()
                {
                    Bias = -0.05,
                    Scale = 0.2,
                    Source0 = new PlainsTerrain(seed, settings)
                }
            }
        };
    }
}

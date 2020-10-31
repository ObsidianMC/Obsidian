using Obsidian.Blocks;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class FrozenRiverDecorator
    {
        public static void Decorate(Chunk chunk, double terrainY, (int x, int z) pos, OverworldNoise noise)
        {
            if (terrainY == 61) // rivers at sea level
            {
                chunk.SetBlock(pos.x, 61, pos.z, Registry.GetBlock(Materials.FrostedIce));

            }
        }
    }
}

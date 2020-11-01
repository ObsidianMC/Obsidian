using Obsidian.Blocks;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class RiverDecorator
    {
        public static void Decorate(Chunk chunk, double terrainY, (int x, int z) pos, OverworldNoise noise)
        {
            if (terrainY > 60) // river above water level
            {
                var waterPos = new Position(pos.x, (int)terrainY, pos.z);
                var sandPos = new Position(pos.x, (int)terrainY-1, pos.z);
                chunk.SetBlock(waterPos, Registry.GetBlock(Materials.Water));
                chunk.SetBlock(sandPos, Registry.GetBlock(Materials.Sand));
            }
        }
    }
}

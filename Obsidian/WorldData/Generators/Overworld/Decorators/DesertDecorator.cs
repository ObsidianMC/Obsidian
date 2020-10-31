using Obsidian.Blocks;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class DesertDecorator
    {
        public static void Decorate(Chunk chunk, double terrainY, (int x, int z) pos, OverworldNoise noise)
        {
            int worldX = (chunk.X << 4) + pos.x;
            int worldZ = (chunk.Z << 4) + pos.z;
            
            var sand = Registry.GetBlock(Materials.Sand);
            var sandstone = Registry.GetBlock(Materials.Sandstone);
            var deadbush = Registry.GetBlock(Materials.DeadBush);
            var cactus = Registry.GetBlock(Materials.Cactus);


            for (int y = 0; y>-4; y--)
                chunk.SetBlock(pos.x, y+(int)terrainY, pos.z, sand);
            for (int y = -4; y > -7; y--)
                chunk.SetBlock(pos.x, y + (int)terrainY, pos.z, sandstone);

            var bushNoise = noise.Decoration(worldX * 0.1, 0, worldZ * 0.1);
            if (bushNoise > 0 && bushNoise < 0.1) // 10% chance for bush
                chunk.SetBlock(pos.x, 1 + (int)terrainY, pos.z, deadbush);

            var cactusNoise = noise.Decoration(worldX * 0.1, 1, worldZ * 0.1);
            if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
                chunk.SetBlock(pos.x, 1 + (int)terrainY, pos.z, cactus);
        }
    }
}

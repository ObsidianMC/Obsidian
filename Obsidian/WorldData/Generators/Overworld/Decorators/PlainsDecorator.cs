using Obsidian.Blocks;
using Obsidian.Util.DataTypes;
using Obsidian.Util.Registry;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class PlainsDecorator
    {
        public static void Decorate(Chunk chunk, double terrainY, (int x, int z) pos, OverworldNoise noise)
        {
            int worldX = (chunk.X << 4) + pos.x;
            int worldZ = (chunk.Z << 4) + pos.z;
            var grassNoise = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
            if(grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
                chunk.SetBlock(pos.x, (int)terrainY + 1, pos.z, Registry.GetBlock(Materials.Grass));

            var poppyNoise = noise.Decoration(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
            if (poppyNoise > 1.7)
                chunk.SetBlock(pos.x, (int)terrainY + 1, pos.z, Registry.GetBlock(Materials.Poppy));

            var dandyNoise = noise.Decoration(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
            if (dandyNoise > 1.7)
                chunk.SetBlock(pos.x, (int)terrainY + 1, pos.z, Registry.GetBlock(Materials.Dandelion));

            var cornFlowerNoise = noise.Decoration(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
            if (cornFlowerNoise > 1.7)
                chunk.SetBlock(pos.x, (int)terrainY + 1, pos.z, Registry.GetBlock(Materials.Cornflower));

            var azureNoise = noise.Decoration(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
            if (azureNoise > 1.7)
                chunk.SetBlock(pos.x, (int)terrainY + 1, pos.z, Registry.GetBlock(Materials.AzureBluet));

            var treeNoise = noise.Decoration(worldX * 0.1, 13, worldZ * 0.1);
            var treeHeight = TreeHeight(treeNoise);
            if (treeHeight > 0)
            {
                for (int y = 1; y<=treeHeight; y++)
                {
                    chunk.SetBlock(pos.x, (int)terrainY + y, pos.z, Registry.GetBlock(Materials.OakLog)); //TODO orientation
                }
                //Leaves
                for (int y = treeHeight + 1; y > treeHeight - 2; y--)
                {
                    for (int x = Math.Max(0, pos.x - 2); x <= Math.Min(15, pos.x + 2); x++)
                    {
                        for (int z = Math.Max(0, pos.z - 2); z <= Math.Min(15, pos.z + 2); z++)
                        {
                            chunk.SetBlock(x, (int)terrainY + y, z, Registry.GetBlock(Materials.OakLeaves));
                        }
                    }
                }
            }

            // If on the edge of the chunk, check if neighboring chunks need leaves.
            if (pos.x == 0)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration((worldX - 1) * 0.1, 13, worldZ * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration((worldX - 2) * 0.1, 13, worldZ * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int) noise.Terrain(worldX - 1, worldZ);

                for (int x = 0; x < rowsToDraw; x++)
                {
                    for (int z = Math.Max(0, pos.z - 2); z <= Math.Min(15, pos.z + 2); z++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            chunk.SetBlock(x, y, z, Registry.GetBlock(Materials.OakLeaves));
                        }
                    }
                }
            }
            else if (pos.x == 15)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration((worldX + 1) * 0.1, 13, worldZ * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration((worldX + 2) * 0.1, 13, worldZ * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX + 1, worldZ);

                for (int x = 15; x > 15 - rowsToDraw; x--)
                {
                    for (int z = Math.Max(0, pos.z - 2); z <= Math.Min(15, pos.z + 2); z++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            chunk.SetBlock(x, y, z, Registry.GetBlock(Materials.OakLeaves));
                        }
                    }
                }
            }
            if (pos.z == 0)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ - 1) * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ - 2) * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX, worldZ - 1);

                for (int z = 0; z < rowsToDraw; z++)
                {
                    for (int x = Math.Max(0, pos.x - 2); x <= Math.Min(15, pos.x + 2); x++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            chunk.SetBlock(x, y, z, Registry.GetBlock(Materials.OakLeaves));
                        }
                    }
                }
            }
            else if (pos.z == 15)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ + 1) * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ + 2) * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX, worldZ + 1);

                for (int z = 15; z > 15 - rowsToDraw; z--)
                {
                    for (int x = Math.Max(0, pos.x - 2); x <= Math.Min(15, pos.x + 2); x++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            chunk.SetBlock(x, y, z, Registry.GetBlock(Materials.OakLeaves));
                        }
                    }
                }
            }
        }

        private static int TreeHeight(double value)  
        {
            return value > 0.04 && value < 0.07 ? (int) (value * 100) : 0;
        }
    }
}

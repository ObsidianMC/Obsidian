using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using Obsidian.WorldData.Generators.Overworld.Terrain;
using System;
using System.Linq;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public static class OverworldDecorator
    {
        public static void Decorate(Chunk chunk, double[,] terrainHeightMap, OverworldTerrain ot, World world)
        {
            var noise = new TerrainNoise(ot.settings);
            for (int x = 0; x < 16; x++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var b = ChunkBiome.GetBiome((chunk.X << 4) + x, (chunk.Z << 4) + z, ot);
                    var blockPos = new Vector(x, (int)terrainHeightMap[x, z], z);
                    IDecorator decorator = DecoratorFactory.GetDecorator(b, chunk, blockPos, noise);

                    decorator.Decorate();
                    GenerateTrees(world, blockPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, noise);
                }
            }
        }

        public static void GrowTree(Vector position, BaseTree tree, int? heightOffset = null)
        {
            int offset;
            if (heightOffset is null)
                offset = new Random().Next(-2, 2);
            else
                offset = (int)heightOffset;
            tree.GenerateTree(position, offset);
        }
        
        private static void GenerateTrees(World world, Vector pos, DecoratorFeatures features, TerrainNoise noise)
        {
            foreach (var (tree, index) in features.Trees.Select((value, i) => (value, i)))
            {
                if (tree.Frequency == 0) { continue; }
                var treeInstance = Activator.CreateInstance(tree.TreeType, world) as BaseTree;

                // Use a different noisemap for each tree type by setting another Y value.
                var noiseVal = noise.Decoration(pos.X, -45 + (index * 10), pos.Z);
                var freq = tree.Frequency/100.0;
                bool isTree = noiseVal > 0.8 && noiseVal <= freq + 0.8;
                if (!isTree) { continue; }

                int heightVariance = (int) (((noiseVal - 0.8) * 100) - (freq/2));
                GrowTree(pos, treeInstance, heightVariance);
            }
        }
    }
}

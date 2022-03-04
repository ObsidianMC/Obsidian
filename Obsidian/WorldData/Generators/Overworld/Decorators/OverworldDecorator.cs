using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;
using Obsidian.WorldData.Generators.Overworld.Features.Flora;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public static class OverworldDecorator
{
    public static async Task DecorateAsync(Chunk chunk, double[,] terrainHeightMap, OverworldTerrain ot, World world)
    {
        var noise = new TerrainNoise(ot.settings);
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                var chunkPos = new Vector(x, (int)terrainHeightMap[x, z], z);
                var worldPos = new Vector((chunk.X << 4) + x, chunkPos.Y, (chunk.Z << 4) + z);
                var biome = (Biomes)ot.GetBiome(worldPos.X, worldPos.Z);
                IDecorator decorator = DecoratorFactory.GetDecorator(biome, chunk, chunkPos, noise);

                decorator.Decorate();
                await GenerateTreesAsync(world, chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, noise);
                await GenerateFloraAsync(world, chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, noise);
            }
        }
    }

    private static async Task GenerateFloraAsync(World world, Vector pos, DecoratorFeatures features, TerrainNoise noise)
    {
        foreach (var (flora, index) in features.Flora.Select((value, i) => (value, i)))
        {
            if (flora.Frequency == 0) { continue; }
            var floraInstance = Activator.CreateInstance(flora.FloraType, world) as BaseFlora;
            if (floraInstance is null) { continue; }

            var noiseVal = noise.Decoration(pos.X, -33 + (index * 22), pos.Z);
            var freq = flora.Frequency / 200.0;
            bool isFlora = noiseVal > 0.9 && noiseVal <= freq + 0.9;
            if (!isFlora) { continue; }

            await floraInstance.GenerateFloraAsync(pos, noise.settings.Seed, flora.Radius, flora.Density);
        }
    }

    public static async Task GrowTreeAsync(Vector position, BaseTree tree, int? heightOffset = null)
    {
        int offset;
        if (heightOffset is null)
            offset = new Random().Next(-2, 2);
        else
            offset = (int)heightOffset;
        await tree.TryGenerateTreeAsync(position, offset);
    }

    private static async Task GenerateTreesAsync(World world, Vector pos, DecoratorFeatures features, TerrainNoise noise)
    {
        foreach (var (tree, index) in features.Trees.Select((value, i) => (value, i)))
        {
            if (tree.Frequency == 0) { continue; }
            var treeInstance = Activator.CreateInstance(tree.TreeType, world) as BaseTree;

            // Use a different noisemap for each tree type by setting another Y value.
            var noiseVal = noise.Decoration(pos.X, -45 + (index * 10), pos.Z);
            var freq = tree.Frequency / 100.0;
            bool isTree = noiseVal > 0.8 && noiseVal <= freq + 0.8;
            if (!isTree) { continue; }

            int heightVariance = (int)(((noiseVal - 0.8) * 100) - (freq / 2));
            await GrowTreeAsync(pos, treeInstance, heightVariance);
        }
    }
}

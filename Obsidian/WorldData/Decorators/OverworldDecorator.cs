using Obsidian.ChunkData;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;
using System.Diagnostics;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Decorators;

public static class OverworldDecorator
{
    private static readonly ConcurrentDictionary<Type, Func<GenHelper, IChunk, BaseFlora>> floraCache = new();
    private static readonly ConcurrentDictionary<Type, Func<GenHelper, IChunk, BaseTree>> treeCache = new();

    private static readonly Type[] argumentCache = [typeof(GenHelper), typeof(IChunk)];
    public static readonly ParameterExpression[] expressionParameters = argumentCache.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    static OverworldDecorator()
    {
        var asm = typeof(OverworldDecorator).Assembly;

        var floras = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseFlora)));
        var trees = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseTree)));

        foreach (var floraType in floras)
        {
            var ctor = floraType.GetConstructor(argumentCache);

            var expression = Expression.New(ctor, expressionParameters);
            var lambda = Expression.Lambda<Func<GenHelper, IChunk, BaseFlora>>(expression, expressionParameters);

            var compiledLamda = lambda.Compile();

            floraCache.TryAdd(floraType, compiledLamda);
        }

        foreach(var treeType in trees)
        {
            var ctor = treeType.GetConstructor(argumentCache);

            var expression = Expression.New(ctor, expressionParameters);
            var lambda = Expression.Lambda<Func<GenHelper, IChunk, BaseTree>>(expression, expressionParameters);

            var compiledLamda = lambda.Compile();

            treeCache.TryAdd(treeType, compiledLamda);
        }
    }

    public static async Task DecorateAsync(IChunk chunk, GenHelper helper)
    {
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int y = chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);
                var chunkPos = new Vector(x, y, z);
                var biome = (Biome)helper.Noise.Biome.GetValue((chunk.X << 4) + x, y, (chunk.Z << 4) + z);
                var decorator = DecoratorFactory.GetDecorator(biome, chunk, chunkPos, helper);

                decorator.Decorate();
                await GenerateTreesAsync(chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, helper, chunk);
                await GenerateFloraAsync(chunkPos + (chunk.X << 4, 0, chunk.Z << 4), decorator.Features, helper, chunk);
            }
        }
    }

    internal static async Task GenerateFloraAsync(Vector pos, DecoratorFeatures features, GenHelper helper, IChunk chunk)
    {
        for(int i = 0; i < features.Flora.Count; i++)
        {
            var flora = features.Flora[i];

            if (flora.Frequency == 0)
                continue;

            if (!floraCache.TryGetValue(flora.FloraType, out var floraFactory))
                throw new UnreachableException();

            var floraInstance = floraFactory(helper, chunk);

            var noiseVal = helper.Noise.Decoration.GetValue(pos.X, -33 + (i * 22), pos.Z);
            var freq = flora.Frequency / 200.0;
            bool isFlora = noiseVal > 0.9 && noiseVal <= freq + 0.9;
            if (!isFlora) { continue; }

            await floraInstance.GenerateFloraAsync(pos, helper.Seed, flora.Radius, flora.Density);
        }

    }

    public static async Task GrowTreeAsync(Vector position, BaseTree tree, int? heightOffset = null)
    {
        var offset = heightOffset is null ? Globals.Random.Next(-2, 2) : (int)heightOffset;
        await tree.TryGenerateTreeAsync(position, offset);
    }

    internal static async Task GenerateTreesAsync(Vector pos, DecoratorFeatures features, GenHelper helper, IChunk chunk)
    {
        for(int i = 0; i < features.Trees.Count; i++)
        {
            var tree = features.Trees[i];

            if (tree.Frequency == 0)
                continue;

            if (!treeCache.TryGetValue(tree.TreeType, out var treeFactory))
                throw new UnreachableException();

            var treeInstance = treeFactory(helper, chunk);

            // Use a different noisemap for each tree type by setting another Y value.
            var noiseVal = helper.Noise.Decoration.GetValue(pos.X, -45 + (i * 10), pos.Z);
            var freq = tree.Frequency / 100.0;
            bool isTree = noiseVal > 0.8 && noiseVal <= freq + 0.8;
            if (!isTree) { continue; }

            int heightVariance = (int)(((noiseVal - 0.8) * 100) - (freq / 2));
            await GrowTreeAsync(pos, treeInstance, heightVariance);
        }
    }
}

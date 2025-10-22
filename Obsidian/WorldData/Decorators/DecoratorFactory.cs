using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Generators;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Decorators;

public static class DecoratorFactory
{
    private static readonly Type[] argumentCache = [typeof(BiomeCodec), typeof(IChunk), typeof(Vector), typeof(GenHelper)];
    public static readonly ParameterExpression[] expressionParameters = argumentCache.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    private static readonly ConcurrentDictionary<int, Func<BiomeCodec, IChunk, Vector, GenHelper, BaseDecorator>> decoratorFactory = new();

    static DecoratorFactory()
    {
        var asm = typeof(DecoratorFactory).Assembly;

        var decorators = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseDecorator)));

        foreach (var decorator in decorators)
        {
            var name = decorator.Name.Replace("Decorator", string.Empty);

            if (CodecRegistry.TryGetBiome($"minecraft:{name.ToLower()}", out var biome))
            {
                var ctor = decorator.GetConstructor(argumentCache);

                var expression = Expression.New(ctor, expressionParameters);

                var lambda = Expression.Lambda<Func<BiomeCodec, IChunk, Vector, GenHelper, BaseDecorator>>(expression, expressionParameters);

                decoratorFactory.TryAdd(biome.Id, lambda.Compile());
            }
        }
    }

    public static BaseDecorator GetDecorator(BiomeCodec b, IChunk chunk, Vector position, GenHelper util) =>
        !decoratorFactory.TryGetValue(b.Id, out var decorator) ? 
        decoratorFactory[CodecRegistry.Biomes.Plains.Id](b, chunk, position, util) : decorator(b, chunk, position, util);
}

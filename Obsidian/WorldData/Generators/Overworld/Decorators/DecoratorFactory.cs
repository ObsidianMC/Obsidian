using System.Linq.Expressions;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public static class DecoratorFactory
{
    private static readonly Type[] argumentCache = new[] { typeof(Biomes), typeof(Chunk), typeof(Vector), typeof(GenHelper) };
    public static readonly ParameterExpression[] expressionParameters = argumentCache.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    private static readonly ConcurrentDictionary<Biomes, Func<Biomes, Chunk, Vector, GenHelper, BaseDecorator>> decoratorFactory = new();

    static DecoratorFactory()
    {
        var asm = typeof(DecoratorFactory).Assembly;

        var decorators = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseDecorator)));

        foreach (var decorator in decorators)
        {
            var name = decorator.Name.Replace("Decorator", string.Empty);

            if (Enum.TryParse<Biomes>(name, out var biome))
            {
                var ctor = decorator.GetConstructor(argumentCache);

                var expression = Expression.New(ctor, expressionParameters);

                var lambda = Expression.Lambda<Func<Biomes, Chunk, Vector, GenHelper, BaseDecorator>>(expression, expressionParameters);

                decoratorFactory.TryAdd(biome, lambda.Compile());
            }
        }
    }

    public static BaseDecorator GetDecorator(Biomes b, Chunk chunk, Vector position, GenHelper util) =>
        !decoratorFactory.TryGetValue(b, out var decorator) ? decoratorFactory[Biomes.Default](b, chunk, position, util) : decorator(b, chunk, position, util);
}

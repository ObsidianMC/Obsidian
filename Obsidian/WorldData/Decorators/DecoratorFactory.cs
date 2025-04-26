using Obsidian.WorldData.Generators;
using System.Linq.Expressions;

namespace Obsidian.WorldData.Decorators;

public static class DecoratorFactory
{
    private static readonly Type[] argumentCache = [typeof(Biome), typeof(IChunk), typeof(Vector), typeof(GenHelper)];
    public static readonly ParameterExpression[] expressionParameters = argumentCache.Select((t, i) => Expression.Parameter(t, $"param{i}")).ToArray();

    private static readonly ConcurrentDictionary<Biome, Func<Biome, IChunk, Vector, GenHelper, BaseDecorator>> decoratorFactory = new();

    static DecoratorFactory()
    {
        var asm = typeof(DecoratorFactory).Assembly;

        var decorators = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(BaseDecorator)));

        foreach (var decorator in decorators)
        {
            var name = decorator.Name.Replace("Decorator", string.Empty);

            if (Enum.TryParse<Biome>(name, out var biome))
            {
                var ctor = decorator.GetConstructor(argumentCache);

                var expression = Expression.New(ctor, expressionParameters);

                var lambda = Expression.Lambda<Func<Biome, IChunk, Vector, GenHelper, BaseDecorator>>(expression, expressionParameters);

                decoratorFactory.TryAdd(biome, lambda.Compile());
            }
        }
    }

    public static BaseDecorator GetDecorator(Biome b, IChunk chunk, Vector position, GenHelper util) =>
        !decoratorFactory.TryGetValue(b, out var decorator) ? decoratorFactory[Biome.Default](b, chunk, position, util) : decorator(b, chunk, position, util);
}

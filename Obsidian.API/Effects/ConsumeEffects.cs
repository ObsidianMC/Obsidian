using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Obsidian.API.Effects;

//TODO the rest of the consume effects.
public static class ConsumeEffects
{
    private static readonly Type consumeEffectInterface = typeof(IConsumeEffect);
    private static readonly ConcurrentDictionary<string, Func<IConsumeEffect>> factory = [];

    public static readonly FrozenDictionary<string, Type> Effects = new Dictionary<string, Type>()
    {
        { "minecraft:apply_effect", typeof(EffectWithProbability) }
    }.ToFrozenDictionary();

    public static IConsumeEffect Compile(string resourceLocation)
    {
        if (factory.TryGetValue(resourceLocation, out var value))
            return value();

        var type = Effects[resourceLocation];

        var ctor = type!.GetConstructor([]);

        var expression = Expression.New(ctor);

        var conversionWithState = Expression.Convert(expression, consumeEffectInterface);
        var lambdaWithState = Expression.Lambda<Func<IConsumeEffect>>(conversionWithState);

        var compiledLamda = lambdaWithState.Compile();

        factory.TryAdd(resourceLocation, compiledLamda);

        return compiledLamda();
    }
}

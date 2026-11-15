using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;

namespace Obsidian.API.Effects;

//TODO the rest of the consume effects.
/// <summary>Creates consume effects from their type ids.</summary>
public static class ConsumeEffects
{
    private static readonly Type consumeEffectInterface = typeof(IConsumeEffect);
    private static readonly ConcurrentDictionary<string, Func<IConsumeEffect>> factory = [];

    /// <summary>Gets the consume effect classes, keyed by type id.</summary>
    public static readonly FrozenDictionary<string, Type> Effects = new Dictionary<string, Type>()
    {
        { "minecraft:apply_effect", typeof(EffectWithProbability) }
    }.ToFrozenDictionary();

    /// <summary>Creates a new instance of the consume effect class registered for a type id.</summary>
    /// <param name="resourceLocation">The effect's type id, a key of <see cref="Effects"/>.</param>
    /// <returns>A new effect with default values.</returns>
    /// <exception cref="KeyNotFoundException">No effect class is registered for
    /// <paramref name="resourceLocation"/>.</exception>
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

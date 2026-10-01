using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers;

/// <summary>
/// An entry of a vanilla weighted list (<c>{"data": ..., "weight": n}</c>).
/// </summary>
public sealed class WeightedEntry<T>
{
    public required T Data { get; init; }

    public required int Weight { get; init; }

    /// <summary>
    /// Picks an entry like vanilla's WeightedList.getRandomOrThrow: one <c>nextInt(totalWeight)</c> draw,
    /// walking the cumulative weights in list order.
    /// </summary>
    public static T Pick(IReadOnlyList<WeightedEntry<T>> entries, IRandomSource random)
    {
        var totalWeight = 0;
        foreach (var entry in entries)
            totalWeight += entry.Weight;

        if (totalWeight <= 0)
            throw new InvalidOperationException("Weighted list has no elements.");

        var target = random.NextInt(totalWeight);
        foreach (var entry in entries)
        {
            target -= entry.Weight;
            if (target < 0)
                return entry.Data;
        }

        throw new InvalidOperationException("Unreachable weighted pick.");
    }
}

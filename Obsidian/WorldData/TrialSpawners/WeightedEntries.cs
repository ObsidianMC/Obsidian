using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// Values with weights, from which one is picked at random: vanilla's <c>WeightedList</c>, as configs save it (a list
/// of <c>{data, weight}</c>).
/// </summary>
/// <typeparam name="T">What the entries hold.</typeparam>
internal sealed class WeightedEntries<T>
{
    public WeightedEntries(ImmutableArray<(T Value, int Weight)> entries)
    {
        this.Entries = entries;
        this.TotalWeight = entries.Sum(entry => entry.Weight);
    }

    public static WeightedEntries<T> Empty { get; } = new([]);

    public ImmutableArray<(T Value, int Weight)> Entries { get; }

    /// <summary>The weights' sum; zero when nothing can be picked.</summary>
    public int TotalWeight { get; }

    public bool IsEmpty => this.TotalWeight == 0;

    /// <summary>
    /// Picks an entry, like vanilla's <c>WeightedList.getRandom</c>: one <c>nextInt</c> over the total weight, which
    /// it draws even for a single entry, and the entry the draw falls in. Draws nothing when the weights sum to zero.
    /// </summary>
    /// <returns>Whether an entry was picked.</returns>
    public bool TryPick(IRandomSource random, out T value)
    {
        value = default!;
        if (this.IsEmpty)
            return false;

        var selection = random.NextInt(this.TotalWeight);
        foreach (var (entry, weight) in this.Entries)
        {
            selection -= weight;
            if (selection < 0)
            {
                value = entry;
                return true;
            }
        }

        return false;
    }
}

namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// The number of successes in <see cref="N"/> trials with probability <see cref="P"/>, like vanilla's binomial provider.
/// </summary>
[LootType("minecraft:binomial")]
public sealed class BinomialNumber : INumberProvider
{
    public required INumberProvider N { get; init; }

    public required INumberProvider P { get; init; }

    public int GetInt(LootContext context)
    {
        var trials = this.N.GetInt(context);
        var probability = this.P.GetFloat(context);
        var successes = 0;
        for (var i = 0; i < trials; i++)
        {
            if (context.Random.NextFloat() < probability)
                successes++;
        }

        return successes;
    }

    public float GetFloat(LootContext context) => this.GetInt(context);
}

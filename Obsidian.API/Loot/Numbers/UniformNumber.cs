namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// A uniformly distributed number between <see cref="Min"/> and <see cref="Max"/>, both inclusive for integers.
/// </summary>
[LootType("minecraft:uniform")]
public sealed class UniformNumber : INumberProvider
{
    /// <summary>
    /// Gets the lowest value, inclusive (vanilla's <c>min</c>).
    /// </summary>
    public required INumberProvider Min { get; init; }

    /// <summary>
    /// Gets the highest value, inclusive for integers (vanilla's <c>max</c>).
    /// </summary>
    public required INumberProvider Max { get; init; }

    /// <summary>
    /// Gets a whole number from <see cref="Min"/> to <see cref="Max"/>, both inclusive; <see cref="Min"/> when it's not
    /// below <see cref="Max"/>.
    /// </summary>
    public int GetInt(LootContext context)
    {
        var min = this.Min.GetInt(context);
        var max = this.Max.GetInt(context);
        return min >= max ? min : context.Random.NextInt(max - min + 1) + min;
    }

    /// <summary>
    /// Gets a number from <see cref="Min"/> up to <see cref="Max"/>; <see cref="Min"/> when it's not below
    /// <see cref="Max"/>.
    /// </summary>
    public float GetFloat(LootContext context)
    {
        var min = this.Min.GetFloat(context);
        var max = this.Max.GetFloat(context);
        return min >= max ? min : context.Random.NextFloat() * (max - min) + min;
    }
}

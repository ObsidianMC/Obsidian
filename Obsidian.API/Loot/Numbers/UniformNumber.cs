namespace Obsidian.API.Loot.Numbers;

/// <summary>
/// A uniformly distributed number between <see cref="Min"/> and <see cref="Max"/>, both inclusive for integers.
/// </summary>
[LootType("minecraft:uniform")]
public sealed class UniformNumber : INumberProvider
{
    public required INumberProvider Min { get; init; }

    public required INumberProvider Max { get; init; }

    public int GetInt(LootContext context)
    {
        var min = this.Min.GetInt(context);
        var max = this.Max.GetInt(context);
        return min >= max ? min : context.Random.NextInt(max - min + 1) + min;
    }

    public float GetFloat(LootContext context)
    {
        var min = this.Min.GetFloat(context);
        var max = this.Max.GetFloat(context);
        return min >= max ? min : context.Random.NextFloat() * (max - min) + min;
    }
}

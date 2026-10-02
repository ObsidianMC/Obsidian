using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Vanilla's <c>RandomSource.create(Mth.getSeed(pos))</c> for code that uses the random right away, as structure processors
/// do for every block they process: one random per thread, reseeded for each position.
/// </summary>
internal static class PositionalRandom
{
    [ThreadStatic]
    private static LegacyRandomSource? random;

    /// <summary>
    /// A random seeded from <paramref name="position"/>, in the state a new one would be in. It's only valid until the next
    /// call on the same thread.
    /// </summary>
    public static LegacyRandomSource At(Vector position)
    {
        var source = random ??= new LegacyRandomSource(0L);
        source.SetSeed(Mth.GetSeed(position.X, position.Y, position.Z));
        return source;
    }
}

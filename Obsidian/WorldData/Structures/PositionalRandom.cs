using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Vanilla's <c>RandomSource.create(Mth.getSeed(pos))</c> for code that only needs the random for a moment, as structure
/// processors do for every block they process: the thread's spare random is reseeded instead of creating one.
/// </summary>
internal static class PositionalRandom
{
    [ThreadStatic]
    private static LegacyRandomSource? spare;

    /// <summary>
    /// A random seeded from <paramref name="position"/>, in the state a new one would be in: the thread's spare one, or a
    /// new one while the spare is rented. Give it back with <see cref="Return"/> once done with it.
    /// </summary>
    public static LegacyRandomSource Rent(Vector position)
    {
        var random = spare ?? new LegacyRandomSource(0L);
        spare = null;
        random.SetSeed(Mth.GetSeed(position.X, position.Y, position.Z));
        return random;
    }

    /// <summary>Makes a rented random the thread's spare again.</summary>
    public static void Return(LegacyRandomSource random) => spare = random;
}

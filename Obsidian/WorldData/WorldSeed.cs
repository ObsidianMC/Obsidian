namespace Obsidian.WorldData;

/// <summary>
/// Reads level seeds.
/// </summary>
internal static class WorldSeed
{
    /// <summary>
    /// Parses a level seed the way vanilla does: a number is used as-is, any other text uses Java's String.hashCode.
    /// </summary>
    public static long Parse(string seed)
    {
        seed = seed.Trim();

        if (long.TryParse(seed, out var numericSeed))
            return numericSeed;

        var hash = 0;
        foreach (var c in seed)
            hash = unchecked(31 * hash + c);

        return hash;
    }
}

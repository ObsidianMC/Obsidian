namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Positional factory produced by <see cref="LegacyRandomSource.ForkPositional"/>.
/// </summary>
public sealed class LegacyPositionalRandomFactory : IPositionalRandomFactory
{
    private readonly long seed;

    public LegacyPositionalRandomFactory(long seed) => this.seed = seed;

    public IRandomSource At(int x, int y, int z) => new LegacyRandomSource(Mth.GetSeed(x, y, z) ^ this.seed);

    public IRandomSource FromHashOf(string name) => new LegacyRandomSource(JavaHashCode(name) ^ this.seed);

    /// <remarks>Vanilla ignores the factory seed here.</remarks>
    public IRandomSource FromSeed(long seed) => new LegacyRandomSource(seed);

    /// <summary>Java's <c>String.hashCode</c> over UTF-16 code units.</summary>
    private static int JavaHashCode(string value)
    {
        var hash = 0;
        foreach (var c in value)
            hash = unchecked(31 * hash + c);

        return hash;
    }
}

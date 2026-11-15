namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Positional factory produced by <see cref="LegacyRandomSource.ForkPositional"/>.
/// </summary>
public sealed class LegacyPositionalRandomFactory : IPositionalRandomFactory
{
    private readonly long seed;

    /// <summary>
    /// Stores the seed used to derive positional and named legacy random sources.
    /// </summary>
    public LegacyPositionalRandomFactory(long seed) => this.seed = seed;

    /// <summary>
    /// Creates a repeatable random source from the coordinates and this factory's seed.
    /// </summary>
    public IRandomSource At(int x, int y, int z) => new LegacyRandomSource(Mth.GetSeed(x, y, z) ^ this.seed);

    /// <summary>
    /// The state of the random <see cref="At"/> creates, without the allocation.
    /// </summary>
    internal JavaLcg StateAt(int x, int y, int z) => new(Mth.GetSeed(x, y, z) ^ this.seed);

    /// <summary>
    /// Creates a repeatable random source by combining the name's hash with this factory's seed.
    /// </summary>
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

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// The random of <see cref="IPositionalRandomFactory.At"/>, without allocating one for xoroshiro and legacy factories. For
/// the hot paths that draw a few values per block. Mutable struct: keep it in a local and never copy it.
/// </summary>
internal struct PositionalRandom
{
    // Exactly one of these is in use: the source for other factories, otherwise the state of the factory's kind.
    private readonly IRandomSource? source;
    private readonly bool legacy;
    private Xoroshiro128PlusPlus state;
    private JavaLcg legacyState;

    public PositionalRandom(IPositionalRandomFactory factory, int x, int y, int z)
    {
        if (factory is XoroshiroPositionalRandomFactory xoroshiro)
        {
            this.state = xoroshiro.StateAt(x, y, z);
        }
        else if (factory is LegacyPositionalRandomFactory legacyFactory)
        {
            this.legacyState = legacyFactory.StateAt(x, y, z);
            this.legacy = true;
        }
        else
        {
            this.source = factory.At(x, y, z);
        }
    }

    public float NextFloat() =>
        this.source is not null ? this.source.NextFloat() : this.legacy ? this.legacyState.NextFloat() : this.state.NextFloat();

    public double NextDouble() =>
        this.source is not null ? this.source.NextDouble() : this.legacy ? this.legacyState.NextDouble() : this.state.NextDouble();

    /// <summary>
    /// A value from 0 up to <paramref name="bound"/>, which must be positive.
    /// </summary>
    public int NextInt(int bound) =>
        this.source is not null ? this.source.NextInt(bound) : this.legacy ? this.legacyState.NextInt(bound) : this.state.NextInt(bound);
}

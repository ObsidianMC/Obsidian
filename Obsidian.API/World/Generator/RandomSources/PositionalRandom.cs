namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// The random of <see cref="IPositionalRandomFactory.At"/>, without allocating one for xoroshiro factories. For the hot
/// paths that draw a few values per block. Mutable struct: keep it in a local and never copy it.
/// </summary>
internal struct PositionalRandom
{
    private readonly IRandomSource? source;
    private Xoroshiro128PlusPlus state;

    public PositionalRandom(IPositionalRandomFactory factory, int x, int y, int z)
    {
        if (factory is XoroshiroPositionalRandomFactory xoroshiro)
            this.state = xoroshiro.StateAt(x, y, z);
        else
            this.source = factory.At(x, y, z);
    }

    public float NextFloat() => this.source is null ? this.state.NextFloat() : this.source.NextFloat();

    public double NextDouble() => this.source is null ? this.state.NextDouble() : this.source.NextDouble();

    /// <inheritdoc cref="Xoroshiro128PlusPlus.NextInt(int)"/>
    public int NextInt(int bound) => this.source is null ? this.state.NextInt(bound) : this.source.NextInt(bound);
}

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// xoroshiro128++ random source used by modern (1.18+) world generation.
/// </summary>
public sealed class XoroshiroRandomSource : IRandomSource
{
    private Xoroshiro128PlusPlus state;
    private MarsagliaPolarGaussian gaussian;

    /// <summary>Creates a source from a 64-bit seed, expanded with <see cref="RandomSupport.UpgradeSeedTo128Bit"/>.</summary>
    public XoroshiroRandomSource(long seed) : this(RandomSupport.UpgradeSeedTo128Bit(seed))
    {
    }

    /// <summary>
    /// Initializes the source from the supplied 128-bit state without further mixing.
    /// </summary>
    public XoroshiroRandomSource(Seed128Bit seed) : this(seed.SeedLo, seed.SeedHi)
    {
    }

    /// <summary>Creates a source from a raw 128-bit state (no mixing).</summary>
    public XoroshiroRandomSource(long seedLo, long seedHi) => this.state = new Xoroshiro128PlusPlus(seedLo, seedHi);

    /// <summary>
    /// Creates a random source seeded by drawing from this source.
    /// </summary>
    public IRandomSource Fork()
    {
        var seedLo = this.state.NextLong();
        var seedHi = this.state.NextLong();
        return new XoroshiroRandomSource(seedLo, seedHi);
    }

    /// <summary>
    /// Creates a positional random factory seeded by drawing from this source.
    /// </summary>
    public IPositionalRandomFactory ForkPositional()
    {
        var seedLo = this.state.NextLong();
        var seedHi = this.state.NextLong();
        return new XoroshiroPositionalRandomFactory(seedLo, seedHi);
    }

    /// <summary>
    /// Resets the random source from a 64-bit seed.
    /// </summary>
    public void SetSeed(long seed)
    {
        var upgraded = RandomSupport.UpgradeSeedTo128Bit(seed);
        this.state = new Xoroshiro128PlusPlus(upgraded.SeedLo, upgraded.SeedHi);
        this.gaussian.Reset();
    }

    /// <summary>
    /// Returns a uniformly distributed 32-bit signed integer.
    /// </summary>
    public int NextInt() => this.state.NextInt();

    /// <summary>
    /// Returns an integer in [0, bound); throws for a nonpositive bound.
    /// </summary>
    public int NextInt(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        return this.state.NextInt(bound);
    }

    /// <summary>
    /// Returns an integer in [origin, bound); throws when origin is not less than bound.
    /// </summary>
    public int NextInt(int origin, int bound)
    {
        if (origin >= bound)
            throw new ArgumentException("bound - origin is non positive", nameof(bound));

        return unchecked(origin + this.NextInt(bound - origin));
    }

    /// <summary>
    /// Returns a random integer between the inclusive minimum and maximum.
    /// </summary>
    public int NextIntBetweenInclusive(int min, int max) => unchecked(this.NextInt(max - min + 1) + min);

    /// <summary>
    /// Returns a random 64-bit signed integer.
    /// </summary>
    public long NextLong() => this.state.NextLong();

    /// <summary>
    /// Returns a random Boolean value.
    /// </summary>
    public bool NextBoolean() => (this.state.NextLong() & 1L) != 0L;

    /// <summary>
    /// Returns a random single-precision value in [0, 1).
    /// </summary>
    public float NextFloat() => this.state.NextFloat();

    /// <summary>
    /// Returns a random double-precision value in [0, 1).
    /// </summary>
    public double NextDouble() => this.state.NextDouble();

    /// <summary>
    /// Returns a normal random sample with mean zero and standard deviation one.
    /// </summary>
    public double NextGaussian() => this.gaussian.Next(this);

    /// <summary>
    /// Returns a triangular sample centered on the mode using the difference of two uniform draws.
    /// </summary>
    public double Triangle(double mode, double deviation) => mode + deviation * (this.NextDouble() - this.NextDouble());

    /// <summary>
    /// Returns a triangular sample centered on the mode using the difference of two uniform draws.
    /// </summary>
    public float Triangle(float mode, float deviation) => mode + deviation * (this.NextFloat() - this.NextFloat());

    /// <summary>
    /// Discards the specified number of draws from this source.
    /// </summary>
    public void ConsumeCount(int count)
    {
        for (var i = 0; i < count; i++)
            this.state.NextLong();
    }
}

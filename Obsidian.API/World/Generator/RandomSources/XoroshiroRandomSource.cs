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

    public XoroshiroRandomSource(Seed128Bit seed) : this(seed.SeedLo, seed.SeedHi)
    {
    }

    /// <summary>Creates a source from a raw 128-bit state (no mixing).</summary>
    public XoroshiroRandomSource(long seedLo, long seedHi) => this.state = new Xoroshiro128PlusPlus(seedLo, seedHi);

    public IRandomSource Fork()
    {
        var seedLo = this.state.NextLong();
        var seedHi = this.state.NextLong();
        return new XoroshiroRandomSource(seedLo, seedHi);
    }

    public IPositionalRandomFactory ForkPositional()
    {
        var seedLo = this.state.NextLong();
        var seedHi = this.state.NextLong();
        return new XoroshiroPositionalRandomFactory(seedLo, seedHi);
    }

    public void SetSeed(long seed)
    {
        var upgraded = RandomSupport.UpgradeSeedTo128Bit(seed);
        this.state = new Xoroshiro128PlusPlus(upgraded.SeedLo, upgraded.SeedHi);
        this.gaussian.Reset();
    }

    public int NextInt() => this.state.NextInt();

    public int NextInt(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        return this.state.NextInt(bound);
    }

    public int NextInt(int origin, int bound)
    {
        if (origin >= bound)
            throw new ArgumentException("bound - origin is non positive", nameof(bound));

        return unchecked(origin + this.NextInt(bound - origin));
    }

    public int NextIntBetweenInclusive(int min, int max) => unchecked(this.NextInt(max - min + 1) + min);

    public long NextLong() => this.state.NextLong();

    public bool NextBoolean() => (this.state.NextLong() & 1L) != 0L;

    public float NextFloat() => this.state.NextFloat();

    public double NextDouble() => this.state.NextDouble();

    public double NextGaussian() => this.gaussian.Next(this);

    public double Triangle(double mode, double deviation) => mode + deviation * (this.NextDouble() - this.NextDouble());

    public float Triangle(float mode, float deviation) => mode + deviation * (this.NextFloat() - this.NextFloat());

    public void ConsumeCount(int count)
    {
        for (var i = 0; i < count; i++)
            this.state.NextLong();
    }
}

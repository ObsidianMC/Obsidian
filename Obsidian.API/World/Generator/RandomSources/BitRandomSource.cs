namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Base for sources that produce values from a <see cref="Next"/> bit generator,
/// with the same derivations as <c>java.util.Random</c> (vanilla <c>BitRandomSource</c>).
/// </summary>
public abstract class BitRandomSource : IRandomSource
{
    private const float FloatMultiplier = 1.0f / (1 << 24);
    private const double DoubleMultiplier = 1.0 / (1L << 53);

    private MarsagliaPolarGaussian gaussian;

    /// <summary>Returns the next <paramref name="bits"/> random bits (1 to 32) in the low bits of the result.</summary>
    public abstract int Next(int bits);

    /// <summary>
    /// Creates a random source seeded by drawing from this source.
    /// </summary>
    public abstract IRandomSource Fork();

    /// <summary>
    /// Creates a positional random factory seeded by drawing from this source.
    /// </summary>
    public abstract IPositionalRandomFactory ForkPositional();

    /// <summary>
    /// Resets the random source from a 64-bit seed.
    /// </summary>
    public abstract void SetSeed(long seed);

    /// <summary>
    /// Returns a uniformly distributed 32-bit signed integer.
    /// </summary>
    public int NextInt() => this.Next(32);

    /// <summary>
    /// Returns an integer in [0, bound); throws for a nonpositive bound.
    /// </summary>
    public int NextInt(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);

        if ((bound & (bound - 1)) == 0)
            return (int)((long)bound * this.Next(31) >> 31);

        int bits, value;
        do
        {
            bits = this.Next(31);
            value = bits % bound;
        }
        while (unchecked(bits - value + (bound - 1)) < 0);

        return value;
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
    public long NextLong()
    {
        var high = this.Next(32);
        var low = this.Next(32);
        return unchecked(((long)high << 32) + low);
    }

    /// <summary>
    /// Returns a random Boolean value.
    /// </summary>
    public bool NextBoolean() => this.Next(1) != 0;

    /// <summary>
    /// Returns a random single-precision value in [0, 1).
    /// </summary>
    public float NextFloat() => this.Next(24) * FloatMultiplier;

    /// <summary>
    /// Returns a random double-precision value in [0, 1).
    /// </summary>
    public double NextDouble()
    {
        var high = this.Next(26);
        var low = this.Next(27);
        return (((long)high << 27) + low) * DoubleMultiplier;
    }

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
            this.NextInt();
    }

    /// <summary>Discards a cached gaussian so the next <see cref="NextGaussian"/> starts a fresh pair.</summary>
    protected void ResetGaussian() => this.gaussian.Reset();
}

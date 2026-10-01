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

    public abstract IRandomSource Fork();

    public abstract IPositionalRandomFactory ForkPositional();

    public abstract void SetSeed(long seed);

    public int NextInt() => this.Next(32);

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

    public int NextInt(int origin, int bound)
    {
        if (origin >= bound)
            throw new ArgumentException("bound - origin is non positive", nameof(bound));

        return unchecked(origin + this.NextInt(bound - origin));
    }

    public int NextIntBetweenInclusive(int min, int max) => unchecked(this.NextInt(max - min + 1) + min);

    public long NextLong()
    {
        var high = this.Next(32);
        var low = this.Next(32);
        return unchecked(((long)high << 32) + low);
    }

    public bool NextBoolean() => this.Next(1) != 0;

    public float NextFloat() => this.Next(24) * FloatMultiplier;

    public double NextDouble()
    {
        var high = this.Next(26);
        var low = this.Next(27);
        return (((long)high << 27) + low) * DoubleMultiplier;
    }

    public double NextGaussian() => this.gaussian.Next(this);

    public double Triangle(double mode, double deviation) => mode + deviation * (this.NextDouble() - this.NextDouble());

    public float Triangle(float mode, float deviation) => mode + deviation * (this.NextFloat() - this.NextFloat());

    public void ConsumeCount(int count)
    {
        for (var i = 0; i < count; i++)
            this.NextInt();
    }

    /// <summary>Discards a cached gaussian so the next <see cref="NextGaussian"/> starts a fresh pair.</summary>
    protected void ResetGaussian() => this.gaussian.Reset();
}

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Adapts an existing System.Random stream for context providers without reseeding or replacing its algorithm.
/// Unlike the vanilla sources, this adapter preserves the caller's .NET sequence.
/// </summary>
internal sealed class SystemRandomSource(Random random) : IRandomSource
{
    private MarsagliaPolarGaussian gaussian;

    public IRandomSource Fork() => new LegacyRandomSource(this.NextLong());

    public IPositionalRandomFactory ForkPositional() => new LegacyPositionalRandomFactory(this.NextLong());

    public void SetSeed(long seed) =>
        throw new NotSupportedException("An externally owned System.Random cannot be reseeded.");

    public int NextInt()
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        random.NextBytes(bytes);
        return BitConverter.ToInt32(bytes);
    }

    public int NextInt(int bound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        return random.Next(bound);
    }

    public int NextInt(int origin, int bound)
    {
        if (origin >= bound)
        {
            throw new ArgumentException("bound - origin is non positive", nameof(bound));
        }

        return random.Next(origin, bound);
    }

    public int NextIntBetweenInclusive(int min, int max)
    {
        var value = random.NextInt64(min, (long)max + 1);
        return (int)value;
    }

    public long NextLong()
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        random.NextBytes(bytes);
        return BitConverter.ToInt64(bytes);
    }

    public bool NextBoolean() => random.Next(2) != 0;

    public float NextFloat() => random.NextSingle();

    public double NextDouble() => random.NextDouble();

    public double NextGaussian() => this.gaussian.Next(this);

    public double Triangle(double mode, double deviation) =>
        mode + deviation * (this.NextDouble() - this.NextDouble());

    public float Triangle(float mode, float deviation) =>
        mode + deviation * (this.NextFloat() - this.NextFloat());

    public void ConsumeCount(int count)
    {
        for (var i = 0; i < count; i++)
        {
            this.NextInt();
        }
    }
}

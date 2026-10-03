namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Port of vanilla's <c>RandomSource</c>. Implementations produce exactly the same sequence as Minecraft
/// for the same seed, so world generation can match vanilla bit-for-bit. Implementations are not thread-safe.
/// </summary>
public interface IRandomSource
{
    /// <summary>Creates an independent random seeded from this one. Advances this source.</summary>
    IRandomSource Fork();

    /// <summary>Creates a factory for deriving randoms from positions, names or seeds. Advances this source.</summary>
    IPositionalRandomFactory ForkPositional();

    /// <summary>Resets this source to the state produced by <paramref name="seed"/>.</summary>
    void SetSeed(long seed);

    /// <summary>Returns a uniformly distributed 32-bit value.</summary>
    int NextInt();

    /// <summary>Returns a value in <c>[0, bound)</c>. <paramref name="bound"/> must be positive.</summary>
    int NextInt(int bound);

    /// <summary>Returns a value in <c>[origin, bound)</c> (vanilla <c>nextInt(int, int)</c>).</summary>
    int NextInt(int origin, int bound);

    /// <summary>Returns a value in <c>[min, max]</c> (vanilla <c>nextIntBetweenInclusive</c>).</summary>
    int NextIntBetweenInclusive(int min, int max);

    long NextLong();

    bool NextBoolean();

    /// <summary>Returns a value in <c>[0, 1)</c> with 24 bits of precision.</summary>
    float NextFloat();

    /// <summary>Returns a value in <c>[0, 1)</c> with 53 bits of precision.</summary>
    double NextDouble();

    /// <summary>Returns a normally distributed value (mean 0, deviation 1) using Marsaglia's polar method.</summary>
    double NextGaussian();

    /// <summary>Returns <c>mode + deviation * (NextDouble() - NextDouble())</c>.</summary>
    double Triangle(double mode, double deviation);

    /// <summary>Returns <c>mode + deviation * (NextFloat() - NextFloat())</c>, computed in single precision.</summary>
    float Triangle(float mode, float deviation);

    /// <summary>Advances the source as if <paramref name="count"/> values had been drawn.</summary>
    void ConsumeCount(int count);
}

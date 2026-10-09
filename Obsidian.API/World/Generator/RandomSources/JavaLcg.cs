namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Raw state of <see cref="LegacyRandomSource"/>, the 48-bit linear congruential generator of <c>java.util.Random</c>.
/// Mutable struct: keep it in a non-readonly field and never copy it.
/// </summary>
/// <remarks>
/// The constants and derivations match <c>java.util.Random</c> exactly, since legacy worlds draw from it in vanilla's order.
/// </remarks>
internal struct JavaLcg
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Increment = 0xBL;
    private const long ModulusMask = (1L << 48) - 1;
    private const float FloatUnit = 1.0f / (1 << 24);
    private const double DoubleUnit = 1.0 / (1L << 53);

    private long seed;

    public JavaLcg(long seed) => this.SetSeed(seed);

    public void SetSeed(long seed) => this.seed = (seed ^ Multiplier) & ModulusMask;

    /// <summary>
    /// The next <paramref name="bits"/> random bits (1 to 32) in the low bits of the result.
    /// </summary>
    public int Next(int bits)
    {
        this.seed = unchecked(this.seed * Multiplier + Increment) & ModulusMask;
        return unchecked((int)(this.seed >> (48 - bits)));
    }

    /// <summary>
    /// A value from 0 up to <paramref name="bound"/>, which must be positive.
    /// </summary>
    public int NextInt(int bound)
    {
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

    public float NextFloat() => this.Next(24) * FloatUnit;

    public double NextDouble()
    {
        var high = this.Next(26);
        var low = this.Next(27);
        return (((long)high << 27) + low) * DoubleUnit;
    }
}

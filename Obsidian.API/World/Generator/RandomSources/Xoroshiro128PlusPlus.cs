using System.Numerics;

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Raw xoroshiro128++ state. Mutable struct: keep it in a non-readonly field and never copy it.
/// </summary>
internal struct Xoroshiro128PlusPlus
{
    private const float FloatUnit = 1.0f / (1 << 24);
    private const double DoubleUnit = 1.0 / (1L << 53);

    private ulong seedLo;
    private ulong seedHi;

    public Xoroshiro128PlusPlus(long seedLo, long seedHi)
    {
        this.seedLo = unchecked((ulong)seedLo);
        this.seedHi = unchecked((ulong)seedHi);

        // An all-zero state would only ever produce zeros.
        if ((this.seedLo | this.seedHi) == 0)
        {
            this.seedLo = unchecked((ulong)RandomSupport.GoldenRatio64);
            this.seedHi = unchecked((ulong)RandomSupport.SilverRatio64);
        }
    }

    public long NextLong()
    {
        var lo = this.seedLo;
        var hi = this.seedHi;
        var result = BitOperations.RotateLeft(lo + hi, 17) + lo;

        hi ^= lo;
        this.seedLo = BitOperations.RotateLeft(lo, 49) ^ hi ^ (hi << 21);
        this.seedHi = BitOperations.RotateLeft(hi, 28);

        return unchecked((long)result);
    }

    public int NextInt() => unchecked((int)this.NextLong());

    /// <summary>
    /// A value from 0 up to <paramref name="bound"/>, which must be positive.
    /// </summary>
    public int NextInt(int bound)
    {
        // Lemire's nearly divisionless bounded random, as in vanilla.
        var product = (ulong)(uint)this.NextInt() * (uint)bound;
        var low = (uint)product;
        if (low < (uint)bound)
        {
            var threshold = unchecked((uint)-bound) % (uint)bound;
            while (low < threshold)
            {
                product = (ulong)(uint)this.NextInt() * (uint)bound;
                low = (uint)product;
            }
        }

        return (int)(product >> 32);
    }

    public float NextFloat() => (this.NextLong() >>> 40) * FloatUnit;

    public double NextDouble() => (this.NextLong() >>> 11) * DoubleUnit;
}

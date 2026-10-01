using System.Numerics;

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Raw xoroshiro128++ state. Mutable struct: keep it in a non-readonly field and never copy it.
/// </summary>
internal struct Xoroshiro128PlusPlus
{
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
}

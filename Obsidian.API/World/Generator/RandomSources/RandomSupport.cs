using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// Seed helpers shared by the random sources (vanilla <c>RandomSupport</c>).
/// </summary>
public static class RandomSupport
{
    /// <summary>64-bit golden ratio, also the Xoroshiro fallback low seed for an all-zero state.</summary>
    public const long GoldenRatio64 = -7046029254386353131L;

    /// <summary>64-bit silver ratio, also the Xoroshiro fallback high seed for an all-zero state.</summary>
    public const long SilverRatio64 = 7640891576956012809L;

    /// <summary>Stafford's "Mix13" 64-bit finalizer.</summary>
    public static long MixStafford13(long value)
    {
        unchecked
        {
            value = (value ^ (value >>> 30)) * -4658895280553007687L;
            value = (value ^ (value >>> 27)) * -7723592293110705685L;
            return value ^ (value >>> 31);
        }
    }

    /// <summary>Expands a 64-bit seed to 128 bits without mixing.</summary>
    public static Seed128Bit UpgradeSeedTo128BitUnmixed(long seed)
    {
        var lo = seed ^ SilverRatio64;
        var hi = unchecked(lo + GoldenRatio64);
        return new Seed128Bit(lo, hi);
    }

    /// <summary>Expands a 64-bit seed to a well-mixed 128-bit seed, as used by <see cref="XoroshiroRandomSource"/>.</summary>
    public static Seed128Bit UpgradeSeedTo128Bit(long seed) => UpgradeSeedTo128BitUnmixed(seed).Mixed();

    /// <summary>Builds a 128-bit seed from the MD5 hash of the UTF-8 bytes of <paramref name="name"/>.</summary>
    public static Seed128Bit SeedFromHashOf(string name)
    {
        // MD5 is used for parity with vanilla, not for security.
        Span<byte> hash = stackalloc byte[MD5.HashSizeInBytes];
        MD5.HashData(Encoding.UTF8.GetBytes(name), hash);
        return new Seed128Bit(BinaryPrimitives.ReadInt64BigEndian(hash), BinaryPrimitives.ReadInt64BigEndian(hash[8..]));
    }
}

/// <summary>A 128-bit seed split into two 64-bit halves.</summary>
public readonly record struct Seed128Bit(long SeedLo, long SeedHi)
{
    public Seed128Bit Xor(long seedLo, long seedHi) => new(this.SeedLo ^ seedLo, this.SeedHi ^ seedHi);

    public Seed128Bit Xor(Seed128Bit other) => this.Xor(other.SeedLo, other.SeedHi);

    public Seed128Bit Mixed() => new(RandomSupport.MixStafford13(this.SeedLo), RandomSupport.MixStafford13(this.SeedHi));
}

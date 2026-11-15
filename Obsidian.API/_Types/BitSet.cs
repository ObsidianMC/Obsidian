namespace Obsidian.API;

/// <summary>
/// A growable set of bits stored in longs, in the layout the protocol uses for bit sets such as chunk light masks.
/// </summary>
public sealed class BitSet
{
    /// <summary>Gets the longs that hold the bits; bit 0 is the lowest bit of the first long.</summary>
    public ReadOnlyMemory<long> DataStorage => data.AsMemory();
    private long[] data = [];

    /// <summary>Creates an empty bit set.</summary>
    public BitSet() { }

    /// <summary>A bit set over the given longs, bit 0 being the lowest bit of the first (Java's <c>BitSet.valueOf</c>).</summary>
    public BitSet(long[] data) => this.data = data;

    /// <summary>Sets or clears a bit, growing the storage when the bit is past its end.</summary>
    public void SetBit(int bitIndex, bool value)
    {
        (var arrayIndex, var mask) = GetBitLoc(bitIndex);

        if (arrayIndex >= data.Length)
            Array.Resize(ref data, arrayIndex + 1);

        if (value)
            data[arrayIndex] |= 1L << mask;
        else
            data[arrayIndex] &= ~(1L << mask);
    }

    /// <summary>Gets a bit; bits past the end of the storage are clear.</summary>
    public bool GetBit(int bitIndex)
    {
        (var arrayIndex, var mask) = GetBitLoc(bitIndex);

        if (arrayIndex >= data.Length)
            return false;

        return (data[arrayIndex] & (1L << mask)) != 0;
    }

    private static (int arrayIndex, int mask) GetBitLoc(int bitIndex) => (bitIndex / 64, bitIndex % 64);
}

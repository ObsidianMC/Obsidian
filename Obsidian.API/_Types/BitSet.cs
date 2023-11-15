namespace Obsidian.API;

public sealed class BitSet
{
    public ReadOnlyMemory<long> DataStorage => data.AsMemory();
    private long[] data = [];

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

    public bool GetBit(int bitIndex)
    {
        (var arrayIndex, var mask) = GetBitLoc(bitIndex);

        if (arrayIndex >= data.Length)
            return false;

        return (data[arrayIndex] & (1L << mask)) != 0;
    }

    private static (int arrayIndex, int mask) GetBitLoc(int bitIndex) => (bitIndex / 64, bitIndex % 64);
}

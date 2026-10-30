namespace Obsidian.API.Inventory.DataComponents;

// Java Float/Double equality preserves signed zero and canonicalizes every NaN payload.
internal static class ComponentFloatingPoint
{
    public static FloatKey Key(float value)
    {
        var bits = float.IsNaN(value) ? 0x7fc00000 : BitConverter.SingleToInt32Bits(value);

        return new FloatKey(bits);
    }

    public static DoubleKey Key(double value)
    {
        var bits = double.IsNaN(value) ? 0x7ff8000000000000L : BitConverter.DoubleToInt64Bits(value);

        return new DoubleKey(bits);
    }

    // Distinct key types keep floating-point NBT tags distinct from integer tags with the same bits.
    internal readonly record struct FloatKey(int Bits);

    internal readonly record struct DoubleKey(long Bits);
}

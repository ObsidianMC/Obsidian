using Obsidian.Nbt;
using System.Numerics;

namespace Obsidian.API.Utilities;

public partial class Extensions
{
    /// <summary>
    /// Reads a numeric field from any numeric tag, converting it the way vanilla's <c>NumericTag</c> does, so data a
    /// command stored as another number type (an int <c>Health</c>, say) still loads.
    /// </summary>
    internal static bool TryGetNumber<T>(this NbtCompound compound, string name, out T value)
        where T : struct, INumberBase<T>
    {
        value = default;
        if (!compound.TryGetTag(name, out var tag))
            return false;

        return tag.TryGetNumber(out value);
    }

    /// <summary>Converts one numeric tag the same way, e.g. for list elements.</summary>
    internal static bool TryGetNumber<T>(this INbtTag tag, out T value) where T : struct, INumberBase<T>
    {
        value = default;
        switch (tag)
        {
            case NbtTag<byte> number:
                value = FromWhole<T>(unchecked((sbyte)number.Value));
                return true;

            case NbtTag<bool> flag:
                value = FromWhole<T>(flag.Value ? 1 : 0);
                return true;

            case NbtTag<short> number:
                value = FromWhole<T>(number.Value);
                return true;

            case NbtTag<int> number:
                value = FromWhole<T>(number.Value);
                return true;

            case NbtTag<long> number:
                value = FromWhole<T>(number.Value);
                return true;

            case NbtTag<float> number:
                value = FromFraction<T>(number.Value, number.Value);
                return true;

            case NbtTag<double> number:
                value = FromFraction<T>(number.Value, (float)number.Value);
                return true;

            default:
                return false;
        }
    }

    // Whole numbers narrow by truncation, like Java's casts.
    private static T FromWhole<T>(long whole) where T : struct, INumberBase<T>
    {
        if (typeof(T) == typeof(float))
            return T.CreateTruncating((float)whole);

        if (typeof(T) == typeof(double))
            return T.CreateTruncating((double)whole);

        return T.CreateTruncating(whole);
    }

    // A long is Java's saturating cast toward zero; narrower types floor to a saturated int first, then truncate.
    private static T FromFraction<T>(double fraction, float single) where T : struct, INumberBase<T>
    {
        if (typeof(T) == typeof(float))
            return T.CreateTruncating(single);

        if (typeof(T) == typeof(double))
            return T.CreateTruncating(fraction);

        var number = double.IsNaN(fraction) ? 0 : fraction;
        if (typeof(T) == typeof(long))
            return T.CreateSaturating(number);

        var whole = (int)Math.Clamp(Math.Floor(number), int.MinValue, int.MaxValue);
        return T.CreateTruncating(whole);
    }
}

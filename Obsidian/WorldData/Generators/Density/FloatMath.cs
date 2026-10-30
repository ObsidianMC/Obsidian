namespace Obsidian.WorldData.Generators.Density;

/// <summary>
/// The single-precision helpers 26.3's density functions use, with vanilla's operation order and Java's semantics.
/// </summary>
internal static class FloatMath
{
    public static float Lerp(float alpha, float start, float end) => start + alpha * (end - start);

    public static float Lerp2(float alpha1, float alpha2, float x00, float x10, float x01, float x11)
    {
        var bottom = Lerp(alpha1, x00, x10);
        var top = Lerp(alpha1, x01, x11);

        return Lerp(alpha2, bottom, top);
    }

    public static float Lerp3(float alpha1, float alpha2, float alpha3, float x000, float x100, float x010, float x110,
        float x001, float x101, float x011, float x111)
    {
        var near = Lerp2(alpha1, alpha2, x000, x100, x010, x110);
        var far = Lerp2(alpha1, alpha2, x001, x101, x011, x111);

        return Lerp(alpha3, near, far);
    }

    public static float Smoothstep(float x) => x * x * x * (x * (x * 6.0f - 15.0f) + 10.0f);

    /// <summary>Java's float clamp (Mth.clamp): NaN passes through to the upper bound check.</summary>
    public static float Clamp(float value, float min, float max) => value < min ? min : MathF.Min(value, max);

    /// <summary>
    /// Java's <c>Math.round(float)</c>, as a float: rounds half up, NaN becomes 0 and the result saturates to the int
    /// range.
    /// </summary>
    public static float Round(float value)
    {
        if (float.IsNaN(value))
            return 0.0f;

        // Exact in double for every float, unlike value + 0.5f.
        var rounded = Math.Floor(value + 0.5);
        var saturated = Math.Clamp(rounded, int.MinValue, int.MaxValue);

        return (int)saturated;
    }

    /// <summary>Java's floor division and modulo for ints, which round towards negative infinity.</summary>
    public static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainderHasOtherSign = (value % divisor != 0) && ((value ^ divisor) < 0);

        return remainderHasOtherSign ? quotient - 1 : quotient;
    }

    public static int FloorMod(int value, int divisor) => value - FloorDiv(value, divisor) * divisor;
}

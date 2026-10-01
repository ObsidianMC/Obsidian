namespace Obsidian.API.World.Generator.RandomSources;

/// <summary>
/// The subset of vanilla's <c>Mth</c> needed for bit-exact world generation.
/// Argument order matches vanilla (the interpolation factor comes first).
/// </summary>
public static class Mth
{
    /// <summary>Vanilla floor. Matches <c>(int)Math.Floor</c> in range; out-of-range values wrap like Java.</summary>
    public static int Floor(double value)
    {
        // Double-to-int casts saturate on .NET 9+, the same as Java.
        var truncated = (int)value;
        return value < truncated ? unchecked(truncated - 1) : truncated;
    }

    /// <summary>Vanilla 64-bit floor.</summary>
    public static long LFloor(double value)
    {
        var truncated = (long)value;
        return value < truncated ? unchecked(truncated - 1L) : truncated;
    }

    public static double Square(double value) => value * value;

    public static double Lerp(double delta, double start, double end) => start + delta * (end - start);

    public static double Lerp2(double deltaX, double deltaY, double x0y0, double x1y0, double x0y1, double x1y1) =>
        Lerp(deltaY, Lerp(deltaX, x0y0, x1y0), Lerp(deltaX, x0y1, x1y1));

    public static double Lerp3(
        double deltaX, double deltaY, double deltaZ,
        double x0y0z0, double x1y0z0, double x0y1z0, double x1y1z0,
        double x0y0z1, double x1y0z1, double x0y1z1, double x1y1z1) =>
        Lerp(deltaZ,
            Lerp2(deltaX, deltaY, x0y0z0, x1y0z0, x0y1z0, x1y1z0),
            Lerp2(deltaX, deltaY, x0y0z1, x1y0z1, x0y1z1, x1y1z1));

    /// <summary>Lerp with <paramref name="delta"/> clamped to <c>[0, 1]</c>.</summary>
    public static double ClampedLerp(double delta, double start, double end) =>
        delta < 0.0 ? start : delta > 1.0 ? end : Lerp(delta, start, end);

    /// <summary>Quintic smoothstep <c>6t^5 - 15t^4 + 10t^3</c> used by Perlin noise.</summary>
    public static double Smoothstep(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    public static double SmoothstepDerivative(double t) => 30.0 * t * t * (t - 1.0) * (t - 1.0);

    /// <summary>Vanilla per-position seed hash (<c>Mth.getSeed</c>), used by positional random factories.</summary>
    public static long GetSeed(int x, int y, int z)
    {
        unchecked
        {
            // x * 3129871 intentionally overflows as a 32-bit multiply before widening, as in vanilla.
            var seed = (long)(x * 3129871) ^ z * 116129781L ^ y;
            seed = seed * seed * 42317861L + seed * 11L;
            return seed >> 16;
        }
    }
}

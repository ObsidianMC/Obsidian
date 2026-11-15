namespace Obsidian.API.Noise;
/// <summary>
/// Provides interpolation and rarity transforms used by noise and density functions.
/// </summary>
public static class MathUtils
{
    /// <summary>
    /// Rounds down to a whole number and converts the result to a 32-bit integer.
    /// </summary>
    public static int Floor(double value) => (int)Math.Floor(value);

    /// <summary>
    /// Evaluates the cubic smoothstep polynomial <c>t²(3 - 2t)</c> without clamping its input.
    /// </summary>
    public static double Smoothstep(double t) => t * t * (3 - 2 * t);

    /// <summary>
    /// Interpolates from start to end using the supplied fraction without clamping.
    /// </summary>
    public static double Lerp(double t, double start, double end) => start + t * (end - start);

    /// <summary>
    /// Interpolates bilinearly between four corner values using X and Y fractions.
    /// </summary>
    public static double Lerp2(double tx, double ty, double v00, double v10, double v01, double v11)
    {
        double xLerpBottom = Lerp(tx, v00, v10); // Interpolate along x-axis for bottom row
        double xLerpTop = Lerp(tx, v01, v11);    // Interpolate along x-axis for top row
        return Lerp(ty, xLerpBottom, xLerpTop);  // Interpolate along y-axis
    }

    /// <summary>
    /// Interpolates trilinearly between eight corner values using X, Y and Z fractions.
    /// </summary>
    public static double Lerp3(double tx, double ty, double tz,
                                double v000, double v100, double v010, double v110,
                                double v001, double v101, double v011, double v111)
    {
        double zLerpBottom = Lerp2(tx, ty, v000, v100, v010, v110); // Interpolate along x and y for z=0
        double zLerpTop = Lerp2(tx, ty, v001, v101, v011, v111);    // Interpolate along x and y for z=1
        return Lerp(tz, zLerpBottom, zLerpTop);                    // Interpolate along z-axis
    }

    /// <summary>
    /// Maps a height interval to a value interval, clamping to the endpoint values outside the height interval.
    /// </summary>
    public static double ClampedMap(double blockY, double fromY, double toY, double fromValue, double toValue) =>
        ClampedLerp(InverseLerp(blockY, fromY, toY), fromValue, toValue);

    /// <summary>
    /// Returns the fraction locating a value between start and end without clamping.
    /// </summary>
    public static double InverseLerp(double t, double start, double end) => (t - start) / (end - start);

    /// <summary>
    /// Interpolates from start to end after clamping the fraction to [0, 1].
    /// </summary>
    public static double ClampedLerp(double t, double start, double end) => t < 0.0 ? start : t > 1.0 ? end : MathUtils.Lerp(t, start, end);

    /// <summary>
    /// Maps a density score to one of 0.5, 0.75, 1, 2 or 3 for the type-2 scaled sampler.
    /// </summary>
    public static double RarityType2(double rarityScore) =>
        rarityScore < -0.75 ? 0.5 :
        rarityScore < -0.5 ? 0.75 :
        rarityScore < 0.5 ? 1.0 :
        rarityScore < 0.75 ? 2.0 : 3.0;
    

    /// <summary>
    /// Maps a density score to one of 0.75, 1, 1.5 or 2 for the type-1 scaled sampler.
    /// </summary>
    public static double RarityType1(double rarityScore) =>
        rarityScore < -0.5 ? 0.75 :
        rarityScore < 0.0 ? 1.0 :
        rarityScore < 0.5 ? 1.5 : 2.0;

}

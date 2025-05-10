namespace Obsidian.API.Noise;
public static class MathUtils
{
    public static int Floor(double value) => (int)Math.Floor(value);

    public static double Smoothstep(double t) => t * t * (3 - 2 * t);

    public static double Lerp(double t, double start, double end) => start + t * (end - start);

    public static double Lerp2(double tx, double ty, double v00, double v10, double v01, double v11)
    {
        double xLerpBottom = Lerp(tx, v00, v10); // Interpolate along x-axis for bottom row
        double xLerpTop = Lerp(tx, v01, v11);    // Interpolate along x-axis for top row
        return Lerp(ty, xLerpBottom, xLerpTop);  // Interpolate along y-axis
    }

    public static double Lerp3(double tx, double ty, double tz,
                                double v000, double v100, double v010, double v110,
                                double v001, double v101, double v011, double v111)
    {
        double zLerpBottom = Lerp2(tx, ty, v000, v100, v010, v110); // Interpolate along x and y for z=0
        double zLerpTop = Lerp2(tx, ty, v001, v101, v011, v111);    // Interpolate along x and y for z=1
        return Lerp(tz, zLerpBottom, zLerpTop);                    // Interpolate along z-axis
    }

    public static double ClampedMap(double blockY, double fromY, double toY, double fromValue, double toValue) => ClampedLerp(InverseLerp(blockY, fromY, toY), fromValue, toValue);

    public static double InverseLerp(double t, double start, double end) => (t - start) / (end - start);

    public static double ClampedLerp(double t, double start, double end) => t < 0.0 ? start : t > 1.0 ? end : MathUtils.Lerp(t, start, end);

    public static double RarityType2(double rarityScore) =>
        rarityScore < -0.75 ? 0.5 :
        rarityScore < -0.5 ? 0.75 :
        rarityScore < 0.5 ? 1.0 :
        rarityScore < 0.75 ? 2.0 : 3.0;
    

    public static double RarityType1(double rarityScore) =>
        rarityScore < -0.5 ? 0.75 :
        rarityScore < 0.0 ? 1.0 :
        rarityScore < 0.5 ? 1.5 : 2.0;

}

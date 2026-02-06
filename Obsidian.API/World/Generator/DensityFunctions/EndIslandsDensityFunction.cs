namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:end_islands")]
public sealed class EndIslandsDensityFunction : IDensityFunction
{
    public string Type => "minecraft:end_islands";

    public double MinValue => -0.84;

    public double MaxValue => 0.56;

    public double GetValue(double x, double y, double z)
    {
        // Simplified End Islands generation - creates circular islands
        // Full implementation would use simplex noise similar to Java's TheEndBiomeSource
        double distanceFromCenter = Math.Sqrt(x * x + z * z);

        if (distanceFromCenter < 100.0)
        {
            // Main island
            return 0.5;
        }

        // Outer islands - create ring pattern with some noise
        double angle = Math.Atan2(z, x);
        double ringDistance = distanceFromCenter / 1000.0;
        double noise = Math.Sin(angle * 12.0) * 0.15 + Math.Cos(ringDistance * 3.14159) * 0.2;

        return ((distanceFromCenter % 250) < 100) ? (0.3 + noise) : (-0.5 + noise);
    }
}

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:mul")]
public sealed class MulDensityFunction : IDensityFunction
{
    public string Type => "minecraft:mul";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue
    {
        get
        {
            double min1 = Argument1.MinValue, min2 = Argument2.MinValue;
            double max1 = Argument1.MaxValue, max2 = Argument2.MaxValue;

            if (min1 > 0.0 && min2 > 0.0)
                return min1 * min2;

            if (max1 < 0.0 && max2 < 0.0)
                return max1 * max2;

            return Math.Min(min1 * max2, max1 * min2);
        }
    }

    public double MaxValue
    {
        get
        {
            double min1 = Argument1.MinValue, min2 = Argument2.MinValue;
            double max1 = Argument1.MaxValue, max2 = Argument2.MaxValue;

            if (min1 > 0.0 && min2 > 0.0)
                return max1 * max2;

            if (max1 < 0.0 && max2 < 0.0)
                return min1 * min2;

            return Math.Max(min1 * min2, max1 * max2);
        }
    }

    public double GetValue(double x, double y, double z) => Argument1.GetValue(x, y, z) * Argument2.GetValue(x, y, z);
}

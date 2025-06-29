namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:range_choice")]
public sealed class RangeChoiceDensityFunction : IDensityFunction
{
    public string Type => "minecraft:range_choice";

    public required IDensityFunction Input { get; init; }

    public required IDensityFunction WhenInRange { get; init; }

    public required IDensityFunction WhenOutOfRange { get; init; }

    public required double MinInclusive { get; init; }

    public required double MaxExclusive { get; init; }

    public double MinValue => Math.Min(WhenInRange.MinValue, WhenOutOfRange.MinValue);

    public double MaxValue =>Math.Max(WhenInRange.MaxValue, WhenOutOfRange.MaxValue);

    public double GetValue(double x, double y, double z)
    {
        var control = Input.GetValue(x, y, z);
        if (control >= MinInclusive && control < MaxExclusive)
            return WhenInRange.GetValue(x, y, z);

        return WhenOutOfRange.GetValue(x, y, z);
    }
}

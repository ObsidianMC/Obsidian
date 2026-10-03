namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:mul")]
public sealed class MulDensityFunction : IDensityFunction
{
    public string Type => "minecraft:mul";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => this.Bounds.Min;

    public double MaxValue => this.Bounds.Max;

    private DensityBounds Bounds => field ??= this.ComputeBounds();

    // The second argument is skipped when the first is zero, matching vanilla.
    public double GetValue(double x, double y, double z)
    {
        var value = this.Argument1.GetValue(x, y, z);
        return value == 0.0 ? 0.0 : value * this.Argument2.GetValue(x, y, z);
    }

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new MulDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });

    private DensityBounds ComputeBounds()
    {
        var (min1, max1, min2, max2) = (this.Argument1.MinValue, this.Argument1.MaxValue, this.Argument2.MinValue, this.Argument2.MaxValue);

        if (min1 > 0.0 && min2 > 0.0)
            return new(min1 * min2, max1 * max2);

        if (max1 < 0.0 && max2 < 0.0)
            return new(max1 * max2, min1 * min2);

        return new(Math.Min(min1 * max2, max1 * min2), Math.Max(min1 * min2, max1 * max2));
    }
}

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:min")]
public sealed class MinDensityFunction : IDensityFunction
{
    public string Type => "minecraft:min";

    public required IDensityFunction Argument1 { get; init; }
    public required IDensityFunction Argument2 { get; init; }

    public double MinValue => Math.Min(this.Argument1.MinValue, this.Argument2.MinValue);

    public double MaxValue => Math.Min(this.Argument1.MaxValue, this.Argument2.MaxValue);

    private DensityBounds Argument2Bounds => field ??= new(this.Argument2.MinValue, this.Argument2.MaxValue);

    // The second argument is skipped when it cannot go lower than the first, matching vanilla.
    public double GetValue(double x, double y, double z)
    {
        var value = this.Argument1.GetValue(x, y, z);
        return value < this.Argument2Bounds.Min ? value : Math.Min(value, this.Argument2.GetValue(x, y, z));
    }

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new MinDensityFunction
    {
        Argument1 = visitor.Map(this.Argument1),
        Argument2 = visitor.Map(this.Argument2)
    });
}

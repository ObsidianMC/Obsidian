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

    public double MinValue => Math.Min(this.WhenInRange.MinValue, this.WhenOutOfRange.MinValue);

    public double MaxValue => Math.Max(this.WhenInRange.MaxValue, this.WhenOutOfRange.MaxValue);

    public double GetValue(double x, double y, double z)
    {
        var control = this.Input.GetValue(x, y, z);
        if (control >= this.MinInclusive && control < this.MaxExclusive)
            return this.WhenInRange.GetValue(x, y, z);

        return this.WhenOutOfRange.GetValue(x, y, z);
    }

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) => visitor.Apply(new RangeChoiceDensityFunction
    {
        Input = visitor.Map(this.Input),
        WhenInRange = visitor.Map(this.WhenInRange),
        WhenOutOfRange = visitor.Map(this.WhenOutOfRange),
        MinInclusive = this.MinInclusive,
        MaxExclusive = this.MaxExclusive
    });
}

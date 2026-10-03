namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:clamp")]
public sealed class ClampDensityFunction : IDensityFunction
{
    public string Type => "minecraft:clamp";

    public required IDensityFunction Input { get; init; }

    public required double Min { get; init; }
    public required double Max { get; init; }

    public double MinValue => this.Min;

    public double MaxValue => this.Max;

    public double GetValue(double x, double y, double z) => Math.Clamp(this.Input.GetValue(x, y, z), this.Min, this.Max);

    public IDensityFunction MapAll(IDensityFunctionVisitor visitor) =>
        visitor.Apply(new ClampDensityFunction { Input = visitor.Map(this.Input), Min = this.Min, Max = this.Max });
}

namespace Obsidian.API.World.Generator.DensityFunctions;

[DensityFunction("minecraft:clamp")]
public sealed class ClampDensityFunction : IDensityFunction
{
    public string Type => "minecraft:clamp";

    public required IDensityFunction Input { get; init; }

    public required double Min { get; init; }
    public required double Max { get; init; }

    public double MinValue => Min;

    public double MaxValue => Max;

    public double GetValue(double x, double y, double z) => Math.Clamp(Input.GetValue(x, y, z), Min, Max);
}
